using System.Reflection;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.DiskPreviewCacheSupport;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class DiskPreviewCacheEntrySpecs
{
    [Fact]
    public void RegistersOneCacheInstanceForRequestsAndMaintenance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateApplicationPaths(Path.GetTempPath()));
        var registrator = new PluginServiceRegistrator();
        IServerApplicationHost applicationHost = DispatchProxy.Create<IServerApplicationHost, ServerApplicationHostSpecs>();
        registrator.RegisterServices(services, applicationHost);
        using ServiceProvider provider = services.BuildServiceProvider();

        IPreviewCache previewCache = provider.GetRequiredService<IPreviewCache>();
        IPreviewCacheMaintenance maintenance = provider.GetRequiredService<IPreviewCacheMaintenance>();

        Assert.Same(previewCache, maintenance);
    }

    [Fact]
    public async Task BuffersExistingPreviewCacheEntryBeforeReturning()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        byte[] originalContent = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9];
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.FinalPath)!);
        await File.WriteAllBytesAsync(fixture.FinalPath, originalContent, CancellationToken.None);

        PreviewCacheResult result = await fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            (_, _) => throw new InvalidOperationException("An existing entry must not be regenerated."),
            CancellationToken.None).WaitAsync(CoordinationTimeout);
        await File.WriteAllBytesAsync(fixture.FinalPath, [9, 9, 9], CancellationToken.None);

        Assert.Equal(PreviewCacheDisposition.Hit, result.Disposition);
        Assert.Equal(originalContent, result.Content.ToArray());
        Assert.Null(result.EncodingTelemetry);
    }

    [Fact]
    public async Task RegeneratesPreviewCacheEntryThatDisappearsBeforeOwnedRead()
    {
        byte[] originalContent = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9];
        byte[] regeneratedContent = [0xFF, 0xD8, 3, 4, 0xFF, 0xD9];
        TemporaryCacheFixture? observedFixture = null;
        Task<PreviewCacheResult>? entryWaiter = null;
        bool deletedEntry = false;
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(checkpoint =>
        {
            if (checkpoint == PreviewCacheCheckpoint.EntryLeaseAcquired && !deletedEntry)
            {
                deletedEntry = true;
                TemporaryCacheFixture activeFixture = Assert.IsType<TemporaryCacheFixture>(observedFixture);
                File.Delete(activeFixture.FinalPath);
                entryWaiter = activeFixture.Cache.GetOrCreateAsync(
                    activeFixture.Identity,
                    (_, _) => throw new InvalidOperationException("A same-entry waiter must observe regeneration."),
                    CancellationToken.None);
                Assert.False(entryWaiter.IsCompleted);
            }
        });
        observedFixture = fixture;
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.FinalPath)!);
        await File.WriteAllBytesAsync(fixture.FinalPath, originalContent, CancellationToken.None);

        PreviewCacheResult miss = await fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(regeneratedContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None).WaitAsync(CoordinationTimeout);
        Task<PreviewCacheResult> completedEntryWaiter = Assert.IsAssignableFrom<Task<PreviewCacheResult>>(entryWaiter);
        PreviewCacheResult hit = await completedEntryWaiter.WaitAsync(CoordinationTimeout);

        Assert.Equal(PreviewCacheDisposition.Miss, miss.Disposition);
        Assert.Equal(PreviewCacheDisposition.Hit, hit.Disposition);
        Assert.Equal(regeneratedContent, miss.Content.ToArray());
        Assert.Equal(
            regeneratedContent,
            await File.ReadAllBytesAsync(fixture.FinalPath, CancellationToken.None));
    }

    [Fact]
    public async Task RetainsSingleEntryOwnerWhileAWaiterCancels()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        var ownerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOwner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] generatedContent = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9];
        Task<PreviewCacheResult> owner = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                ownerStarted.SetResult();
                await releaseOwner.Task.WaitAsync(cancellationToken);
                await destination.WriteAsync(generatedContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None).WaitAsync(CoordinationTimeout);
        await ownerStarted.Task.WaitAsync(CoordinationTimeout);
        using var waiterCancellation = new CancellationTokenSource();
        Task<PreviewCacheResult> waiter = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            (_, _) => throw new InvalidOperationException("A same-entry waiter must not encode concurrently."),
            waiterCancellation.Token);

        waiterCancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => waiter.WaitAsync(CoordinationTimeout));
        }
        finally
        {
            releaseOwner.TrySetResult();
        }

        PreviewCacheResult result = await owner.WaitAsync(CoordinationTimeout);

        Assert.Equal(PreviewCacheDisposition.Miss, result.Disposition);
        Assert.Equal(generatedContent, result.Content.ToArray());
    }

    [Fact]
    public async Task GeneratesIdenticalMissesOnlyOnce()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        var ownerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOwner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] generatedContent = [0xFF, 0xD8, 1, 2, 0xFF, 0xD9];
        int encodingCount = 0;
        Task<PreviewCacheResult> owner = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                Interlocked.Increment(ref encodingCount);
                ownerStarted.SetResult();
                await releaseOwner.Task.WaitAsync(cancellationToken);
                await destination.WriteAsync(generatedContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None);
        await ownerStarted.Task.WaitAsync(CoordinationTimeout);
        Task<PreviewCacheResult> waiter = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            (_, _) => throw new InvalidOperationException("A same-entry waiter must not encode concurrently."),
            CancellationToken.None);

        Assert.False(waiter.IsCompleted);
        releaseOwner.SetResult();
        PreviewCacheResult[] results = await Task.WhenAll(owner, waiter).WaitAsync(CoordinationTimeout);

        Assert.Equal(1, encodingCount);
        Assert.Equal(PreviewCacheDisposition.Miss, results[0].Disposition);
        Assert.Equal(PreviewCacheDisposition.Hit, results[1].Disposition);
        Assert.All(results, result => Assert.Equal(generatedContent, result.Content.ToArray()));
    }

    [Fact]
    public async Task GeneratesDifferentEntriesConcurrently()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        PreviewIdentity secondIdentity = CreateIdentity("f0000000001.jpg");
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] firstContent = [0xFF, 0xD8, 1, 0xFF, 0xD9];
        byte[] secondContent = [0xFF, 0xD8, 2, 0xFF, 0xD9];

        Task<PreviewCacheResult> first = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                firstStarted.SetResult();
                await secondStarted.Task.WaitAsync(cancellationToken);
                await destination.WriteAsync(firstContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None);
        Task<PreviewCacheResult> second = fixture.Cache.GetOrCreateAsync(
            secondIdentity,
            async (destination, cancellationToken) =>
            {
                secondStarted.SetResult();
                await firstStarted.Task.WaitAsync(cancellationToken);
                await destination.WriteAsync(secondContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None);

        PreviewCacheResult[] results = await Task.WhenAll(first, second).WaitAsync(CoordinationTimeout);

        Assert.Equal(firstContent, results[0].Content.ToArray());
        Assert.Equal(secondContent, results[1].Content.ToArray());
    }

    [Fact]
    public async Task WaiterGeneratesAfterOwnerCancellation()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        using var ownerCancellation = new CancellationTokenSource();
        var ownerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] generatedContent = [0xFF, 0xD8, 3, 4, 0xFF, 0xD9];
        Task<PreviewCacheResult> owner = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
                ownerStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable after cancellation.");
            },
            ownerCancellation.Token);
        await ownerStarted.Task.WaitAsync(CoordinationTimeout);
        Task<PreviewCacheResult> waiter = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(generatedContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None);

        ownerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => owner.WaitAsync(CoordinationTimeout));
        PreviewCacheResult result = await waiter.WaitAsync(CoordinationTimeout);
        Assert.Equal(PreviewCacheDisposition.Miss, result.Disposition);
        Assert.Equal(generatedContent, result.Content.ToArray());
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
    }

    [Fact]
    public async Task BuffersGeneratedPreviewCacheEntryBeforeReturning()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        byte[] generatedContent = [0xFF, 0xD8, 7, 8, 0xFF, 0xD9];
        PreviewCacheResult result = await fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(generatedContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None);

        await File.WriteAllBytesAsync(fixture.FinalPath, [9, 9, 9], CancellationToken.None);

        Assert.Equal(PreviewCacheDisposition.Miss, result.Disposition);
        Assert.Equal(generatedContent, result.Content.ToArray());
    }

    [Fact]
    public async Task ServesCompleteOutsideWinnerWithoutOverwritingIt()
    {
        byte[] generated = [1, 2, 3, 4];
        byte[] outsideWinner = [0xFF, 0xD8, 0xFF, 0xD9];
        TemporaryCacheFixture? observedFixture = null;
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(checkpoint =>
        {
            if (checkpoint == PreviewCacheCheckpoint.BeforePublication)
            {
                TemporaryCacheFixture activeFixture = Assert.IsType<TemporaryCacheFixture>(observedFixture);
                File.WriteAllBytes(activeFixture.FinalPath, outsideWinner);
            }
        });
        observedFixture = fixture;
        string? temporaryPath = null;
        var encodingTelemetry = new PreviewEncodingTelemetry(
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(2));

        PreviewCacheResult result = await fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                string directoryPath = Path.GetDirectoryName(fixture.FinalPath)!;
                temporaryPath = Assert.Single(Directory.EnumerateFiles(directoryPath, "*.tmp"));
                await destination.WriteAsync(generated, cancellationToken);
                return encodingTelemetry;
            },
            CancellationToken.None).WaitAsync(CoordinationTimeout);

        Assert.Equal(PreviewCacheDisposition.Hit, result.Disposition);
        Assert.Equal(outsideWinner, result.Content.ToArray());
        Assert.Equal(
            outsideWinner,
            await File.ReadAllBytesAsync(fixture.FinalPath, CancellationToken.None));
        Assert.Equal(encodingTelemetry, result.EncodingTelemetry);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
        string completedTemporaryPath = Assert.IsType<string>(temporaryPath);
        Assert.Equal(Path.GetDirectoryName(fixture.FinalPath), Path.GetDirectoryName(completedTemporaryPath));
        string temporaryName = Path.GetFileName(completedTemporaryPath);
        Assert.StartsWith("f0000000000.", temporaryName, StringComparison.Ordinal);
        Assert.EndsWith(".tmp", temporaryName, StringComparison.Ordinal);
        string randomName = temporaryName[12..^4];
        Assert.True(Guid.TryParseExact(randomName, "N", out _));
        Assert.Equal(randomName.ToLowerInvariant(), randomName);
    }

    [Fact]
    public async Task KeepsPublishedEntryWhenCancellationAbandonsResponse()
    {
        using var cancellation = new CancellationTokenSource();
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(checkpoint =>
        {
            if (checkpoint == PreviewCacheCheckpoint.AfterPublication)
            {
                cancellation.Cancel();
            }
        });
        byte[] generatedContent = [0xFF, 0xD8, 8, 9, 0xFF, 0xD9];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Cache.GetOrCreateAsync(
                fixture.Identity,
                async (destination, cancellationToken) =>
                {
                    await destination.WriteAsync(generatedContent, cancellationToken);
                    return new PreviewEncodingTelemetry(
                        TimeSpan.FromMilliseconds(1),
                        TimeSpan.FromMilliseconds(2));
                },
                cancellation.Token).WaitAsync(CoordinationTimeout));

        Assert.Equal(generatedContent, await File.ReadAllBytesAsync(fixture.FinalPath, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
    }

    [Fact]
    public async Task HoldsTreeAndEntryLeasesUntilResponseBufferingCompletes()
    {
        TemporaryCacheFixture? observedFixture = null;
        Task<PreviewCacheResult>? entryWaiter = null;
        Task<IDisposable>? pruningLease = null;
        bool observedTreeLease = false;
        bool observedEntryLease = false;
        bool startedWaiters = false;
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(checkpoint =>
        {
            if (checkpoint == PreviewCacheCheckpoint.TreeLeaseAcquired)
            {
                observedTreeLease = true;
            }

            if (checkpoint == PreviewCacheCheckpoint.EntryLeaseAcquired)
            {
                Assert.True(observedTreeLease);
                observedEntryLease = true;
            }

            if (checkpoint == PreviewCacheCheckpoint.ResponseBuffered && !startedWaiters)
            {
                startedWaiters = true;
                Assert.True(observedEntryLease);
                TemporaryCacheFixture activeFixture = Assert.IsType<TemporaryCacheFixture>(observedFixture);
                entryWaiter = activeFixture.Cache.GetOrCreateAsync(
                    activeFixture.Identity,
                    (_, _) => throw new InvalidOperationException("A buffered entry must remain owned."),
                    CancellationToken.None);
                Assert.False(entryWaiter.IsCompleted);
                pruningLease = activeFixture.Coordination
                    .AcquireExclusiveAsync(CancellationToken.None)
                    .AsTask();
                Assert.False(pruningLease.IsCompleted);
            }
        });
        observedFixture = fixture;

        PreviewCacheResult result = await fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None).WaitAsync(CoordinationTimeout);

        Task<PreviewCacheResult> completedEntryWaiter = Assert.IsAssignableFrom<Task<PreviewCacheResult>>(entryWaiter);
        Task<IDisposable> completedPruningLease = Assert.IsAssignableFrom<Task<IDisposable>>(pruningLease);
        PreviewCacheResult hit = await completedEntryWaiter.WaitAsync(CoordinationTimeout);
        using IDisposable exclusiveLease = await completedPruningLease.WaitAsync(CoordinationTimeout);
        Assert.Equal(PreviewCacheDisposition.Miss, result.Disposition);
        Assert.Equal(PreviewCacheDisposition.Hit, hit.Disposition);
    }

}
