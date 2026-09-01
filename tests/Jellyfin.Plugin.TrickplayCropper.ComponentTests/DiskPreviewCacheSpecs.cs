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

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class DiskPreviewCacheSpecs
{
    private static readonly TimeSpan coordinationTimeout = TimeSpan.FromSeconds(10);

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
            CancellationToken.None).WaitAsync(coordinationTimeout);
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
            CancellationToken.None).WaitAsync(coordinationTimeout);
        Task<PreviewCacheResult> completedEntryWaiter = Assert.IsAssignableFrom<Task<PreviewCacheResult>>(entryWaiter);
        PreviewCacheResult hit = await completedEntryWaiter.WaitAsync(coordinationTimeout);

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
            CancellationToken.None).WaitAsync(coordinationTimeout);
        await ownerStarted.Task.WaitAsync(coordinationTimeout);
        using var waiterCancellation = new CancellationTokenSource();
        Task<PreviewCacheResult> waiter = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            (_, _) => throw new InvalidOperationException("A same-entry waiter must not encode concurrently."),
            waiterCancellation.Token);

        waiterCancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => waiter.WaitAsync(coordinationTimeout));
        }
        finally
        {
            releaseOwner.TrySetResult();
        }

        PreviewCacheResult result = await owner.WaitAsync(coordinationTimeout);

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
        await ownerStarted.Task.WaitAsync(coordinationTimeout);
        Task<PreviewCacheResult> waiter = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            (_, _) => throw new InvalidOperationException("A same-entry waiter must not encode concurrently."),
            CancellationToken.None);

        Assert.False(waiter.IsCompleted);
        releaseOwner.SetResult();
        PreviewCacheResult[] results = await Task.WhenAll(owner, waiter).WaitAsync(coordinationTimeout);

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

        PreviewCacheResult[] results = await Task.WhenAll(first, second).WaitAsync(coordinationTimeout);

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
        await ownerStarted.Task.WaitAsync(coordinationTimeout);
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
            () => owner.WaitAsync(coordinationTimeout));
        PreviewCacheResult result = await waiter.WaitAsync(coordinationTimeout);
        Assert.Equal(PreviewCacheDisposition.Miss, result.Disposition);
        Assert.Equal(generatedContent, result.Content.ToArray());
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
    }

    [Fact]
    public async Task WaiterGeneratesAfterOwnerFailure()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        var ownerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failOwner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] generatedContent = [0xFF, 0xD8, 5, 6, 0xFF, 0xD9];
        Task<PreviewCacheResult> owner = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
                ownerStarted.SetResult();
                await failOwner.Task.WaitAsync(cancellationToken);
                throw new IOException("Simulated generation failure.");
            },
            CancellationToken.None);
        await ownerStarted.Task.WaitAsync(coordinationTimeout);
        Task<PreviewCacheResult> waiter = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(generatedContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None);

        failOwner.SetResult();

        await Assert.ThrowsAsync<IOException>(() => owner.WaitAsync(coordinationTimeout));
        PreviewCacheResult result = await waiter.WaitAsync(coordinationTimeout);
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
            CancellationToken.None).WaitAsync(coordinationTimeout);

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
    public async Task RejectsEmptyGeneratedPreviewCacheEntryWithoutPublishingIt()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Cache.GetOrCreateAsync(
                fixture.Identity,
                (_, _) => Task.FromResult(
                    new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2))),
                CancellationToken.None).WaitAsync(coordinationTimeout));

        Assert.False(File.Exists(fixture.FinalPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
    }

    [Fact]
    public async Task RemovesTemporaryEntryWhenGenerationFails()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Cache.GetOrCreateAsync(
                fixture.Identity,
                async (destination, cancellationToken) =>
                {
                    await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
                    throw new IOException("Simulated destination failure.");
                },
                CancellationToken.None).WaitAsync(coordinationTimeout));

        Assert.False(File.Exists(fixture.FinalPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
    }

    [Fact]
    public async Task RemovesTemporaryEntryWhenCancelledBeforePublication()
    {
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Cache.GetOrCreateAsync(
                fixture.Identity,
                async (destination, cancellationToken) =>
                {
                    await destination.WriteAsync(new byte[] { 1, 2, 3 }, cancellationToken);
                    cancellation.Cancel();
                    return new PreviewEncodingTelemetry(
                        TimeSpan.FromMilliseconds(1),
                        TimeSpan.FromMilliseconds(2));
                },
                cancellation.Token).WaitAsync(coordinationTimeout));

        Assert.False(File.Exists(fixture.FinalPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
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
                cancellation.Token).WaitAsync(coordinationTimeout));

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
            CancellationToken.None).WaitAsync(coordinationTimeout);

        Task<PreviewCacheResult> completedEntryWaiter = Assert.IsAssignableFrom<Task<PreviewCacheResult>>(entryWaiter);
        Task<IDisposable> completedPruningLease = Assert.IsAssignableFrom<Task<IDisposable>>(pruningLease);
        PreviewCacheResult hit = await completedEntryWaiter.WaitAsync(coordinationTimeout);
        using IDisposable exclusiveLease = await completedPruningLease.WaitAsync(coordinationTimeout);
        Assert.Equal(PreviewCacheDisposition.Miss, result.Disposition);
        Assert.Equal(PreviewCacheDisposition.Hit, hit.Disposition);
    }

    [Fact]
    public async Task RejectsARequestPathThatCrossesADirectoryReparsePoint()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string externalDirectory = Path.Combine(
            Path.GetTempPath(),
            $"trickplay-request-external-{Guid.NewGuid():N}");
        Directory.CreateDirectory(externalDirectory);
        try
        {
            using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();
            Directory.CreateDirectory(fixture.CacheRoot);
            string firstIdentityDirectory = fixture.Identity.RelativePath.Split(Path.DirectorySeparatorChar)[0];
            Directory.CreateSymbolicLink(
                Path.Combine(fixture.CacheRoot, firstIdentityDirectory),
                externalDirectory);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => fixture.Cache.GetOrCreateAsync(
                    fixture.Identity,
                    async (destination, cancellationToken) =>
                    {
                        await destination.WriteAsync(new byte[] { 1 }, cancellationToken);
                        return new PreviewEncodingTelemetry(TimeSpan.Zero, TimeSpan.Zero);
                    },
                    CancellationToken.None).WaitAsync(coordinationTimeout));

            Assert.Empty(Directory.EnumerateFileSystemEntries(externalDirectory));
        }
        finally
        {
            Directory.Delete(externalDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAPluginDirectoryReparsePointForRequestsAndCleanup()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string externalDirectory = Path.Combine(
            Path.GetTempPath(),
            $"trickplay-plugin-external-{Guid.NewGuid():N}");
        Directory.CreateDirectory(externalDirectory);
        string externalEntryPath = Path.Combine(externalDirectory, "f0000000000.jpg");
        await File.WriteAllBytesAsync(externalEntryPath, [9], CancellationToken.None);
        var logger = new RecordingLogger<DiskPreviewCache>();
        try
        {
            using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
                static _ => { },
                TimeProvider.System,
                logger);
            Directory.CreateSymbolicLink(fixture.PluginRoot, externalDirectory);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => fixture.Cache.GetOrCreateAsync(
                    fixture.Identity,
                    (_, _) => throw new InvalidOperationException("A reparse path must fail before generation."),
                    CancellationToken.None).WaitAsync(coordinationTimeout));
            await fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
                .WaitAsync(coordinationTimeout);

            Assert.True(File.Exists(externalEntryPath));
            RecordedLog warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.Equal(fixture.PluginRoot, warning.Properties["CachePath"]);
        }
        finally
        {
            Directory.Delete(externalDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAFinalEntryThatBecomesAReparsePointBeforePublication()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string externalPath = Path.Combine(
            Path.GetTempPath(),
            $"trickplay-request-external-{Guid.NewGuid():N}.jpg");
        byte[] externalContent = [9, 8, 7];
        await File.WriteAllBytesAsync(externalPath, externalContent, CancellationToken.None);
        try
        {
            using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create();

            await Assert.ThrowsAsync<InvalidDataException>(
                () => fixture.Cache.GetOrCreateAsync(
                    fixture.Identity,
                    async (destination, cancellationToken) =>
                    {
                        await destination.WriteAsync(new byte[] { 1 }, cancellationToken);
                        File.CreateSymbolicLink(fixture.FinalPath, externalPath);
                        return new PreviewEncodingTelemetry(TimeSpan.Zero, TimeSpan.Zero);
                    },
                    CancellationToken.None).WaitAsync(coordinationTimeout));

            Assert.Equal(externalContent, await File.ReadAllBytesAsync(externalPath, CancellationToken.None));
        }
        finally
        {
            File.Delete(externalPath);
        }
    }

    [Fact]
    public async Task DeletesOnlyEligibleFilesAtTheFixedRunBoundary()
    {
        DateTimeOffset boundary = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var logger = new RecordingLogger<DiskPreviewCache>();
        var timeProvider = new FixedTimeProvider(boundary);
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            static _ => { },
            timeProvider,
            logger);
        string directoryPath = Path.GetDirectoryName(fixture.FinalPath)!;
        Directory.CreateDirectory(directoryPath);
        string temporaryPath = Path.Combine(
            directoryPath,
            "f0000000001.00000000000000000000000000000000.tmp");
        string unknownJpegPath = Path.Combine(directoryPath, "notes.jpg");
        string unparseableTemporaryPath = Path.Combine(directoryPath, "f0000000002.random.tmp");
        string laterPath = Path.Combine(directoryPath, "f0000000003.jpg");
        foreach (string path in new[] { fixture.FinalPath, temporaryPath, unknownJpegPath, unparseableTemporaryPath })
        {
            await File.WriteAllBytesAsync(path, [1], CancellationToken.None);
            File.SetLastWriteTimeUtc(path, boundary.AddMinutes(-1).UtcDateTime);
        }

        await File.WriteAllBytesAsync(laterPath, [1], CancellationToken.None);
        File.SetLastWriteTimeUtc(laterPath, boundary.AddMinutes(1).UtcDateTime);
        var progress = new RecordingProgress();

        await fixture.Cache.ClearAsync(progress, CancellationToken.None).WaitAsync(coordinationTimeout);

        Assert.False(File.Exists(fixture.FinalPath));
        Assert.False(File.Exists(temporaryPath));
        Assert.True(File.Exists(unknownJpegPath));
        Assert.False(File.Exists(unparseableTemporaryPath));
        Assert.True(File.Exists(laterPath));
        Assert.Equal(1, timeProvider.GetUtcNowCallCount);
        Assert.Equal(new double[] { 0, 100 }, progress.Values);
        RecordedLog summary = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Equal(3, summary.Properties["DeletedFiles"]);
        Assert.Equal(0, summary.Properties["FailedFiles"]);
        Assert.Equal(0, summary.Properties["SkippedChangedFiles"]);
        Assert.Equal(false, summary.Properties["Cancelled"]);
    }

    [Fact]
    public async Task DeletesAnUnparseableTemporaryOnlyAfterRequestsReleaseTheCacheTree()
    {
        DateTimeOffset boundary = new(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            static _ => { },
            new FixedTimeProvider(boundary),
            NullLogger<DiskPreviewCache>.Instance);
        string directoryPath = Path.GetDirectoryName(fixture.FinalPath)!;
        Directory.CreateDirectory(directoryPath);
        string unparseableTemporaryPath = Path.Combine(directoryPath, "orphan.tmp");
        await File.WriteAllBytesAsync(unparseableTemporaryPath, [1], CancellationToken.None);
        File.SetLastWriteTimeUtc(unparseableTemporaryPath, boundary.AddMinutes(-1).UtcDateTime);
        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PreviewCacheResult> request = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                writerStarted.SetResult();
                await releaseWriter.Task.WaitAsync(cancellationToken);
                await destination.WriteAsync(new byte[] { 1 }, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.Zero, TimeSpan.Zero);
            },
            CancellationToken.None);
        await writerStarted.Task.WaitAsync(coordinationTimeout);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new CallbackProgress(value =>
        {
            if (value == 0)
            {
                cleanupStarted.TrySetResult();
            }
        });

        Task cleanup = Task.Run(() => fixture.Cache.ClearAsync(progress, CancellationToken.None));
        await cleanupStarted.Task.WaitAsync(coordinationTimeout);

        Assert.False(cleanup.IsCompleted);
        Assert.True(File.Exists(unparseableTemporaryPath));
        releaseWriter.SetResult();
        await Task.WhenAll(request, cleanup).WaitAsync(coordinationTimeout);
        Assert.False(File.Exists(unparseableTemporaryPath));
    }

    [Fact]
    public async Task PrunesEmptyDirectoriesBottomUpWithoutRemovingUnknownFiles()
    {
        var logger = new RecordingLogger<DiskPreviewCache>();
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            static _ => { },
            TimeProvider.System,
            logger);
        string entryDirectory = Path.GetDirectoryName(fixture.FinalPath)!;
        Directory.CreateDirectory(entryDirectory);
        await File.WriteAllBytesAsync(fixture.FinalPath, [1], CancellationToken.None);
        string preservedDirectory = Path.Combine(fixture.CacheRoot, "preserved");
        Directory.CreateDirectory(preservedDirectory);
        string unknownPath = Path.Combine(preservedDirectory, "operator-note.txt");
        await File.WriteAllTextAsync(unknownPath, "keep", CancellationToken.None);

        await fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
            .WaitAsync(coordinationTimeout);

        Assert.False(Directory.Exists(entryDirectory));
        Assert.True(File.Exists(unknownPath));
        Assert.True(Directory.Exists(fixture.CacheRoot));
        RecordedLog summary = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Equal(3, summary.Properties["DeletedDirectories"]);
        Assert.Equal(0, summary.Properties["FailedDirectories"]);
    }

    [Fact]
    public async Task SkipsAChangedCandidateAfterTakingItsEntryLock()
    {
        TemporaryCacheFixture? observedFixture = null;
        bool changedCandidate = false;
        var logger = new RecordingLogger<DiskPreviewCache>();
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            checkpoint =>
            {
                if (checkpoint == PreviewCacheCheckpoint.CleanupCandidateCaptured && !changedCandidate)
                {
                    changedCandidate = true;
                    TemporaryCacheFixture activeFixture = Assert.IsType<TemporaryCacheFixture>(observedFixture);
                    File.WriteAllBytes(activeFixture.FinalPath, [1, 2]);
                }
            },
            TimeProvider.System,
            logger);
        observedFixture = fixture;
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.FinalPath)!);
        await File.WriteAllBytesAsync(fixture.FinalPath, [1], CancellationToken.None);

        await fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
            .WaitAsync(coordinationTimeout);

        Assert.Equal(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(fixture.FinalPath, CancellationToken.None));
        RecordedLog summary = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Equal(0, summary.Properties["DeletedFiles"]);
        Assert.Equal(1, summary.Properties["SkippedChangedFiles"]);
    }

    [Fact]
    public async Task SkipsACandidateThatBecomesAFileReparsePoint()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string externalPath = Path.Combine(
            Path.GetTempPath(),
            $"trickplay-cleanup-external-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(externalPath, [1], CancellationToken.None);
        TemporaryCacheFixture? observedFixture = null;
        bool replacedCandidate = false;
        var logger = new RecordingLogger<DiskPreviewCache>();
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            checkpoint =>
            {
                if (checkpoint == PreviewCacheCheckpoint.CleanupCandidateCaptured && !replacedCandidate)
                {
                    replacedCandidate = true;
                    TemporaryCacheFixture activeFixture = Assert.IsType<TemporaryCacheFixture>(observedFixture);
                    File.Delete(activeFixture.FinalPath);
                    File.CreateSymbolicLink(activeFixture.FinalPath, externalPath);
                }
            },
            TimeProvider.System,
            logger);
        observedFixture = fixture;
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.FinalPath)!);
        await File.WriteAllBytesAsync(fixture.FinalPath, [1], CancellationToken.None);

        try
        {
            await fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
                .WaitAsync(coordinationTimeout);

            Assert.True(File.Exists(fixture.FinalPath));
            Assert.True(File.Exists(externalPath));
            RecordedLog warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.Equal(fixture.FinalPath, warning.Properties["CachePath"]);
        }
        finally
        {
            File.Delete(externalPath);
        }
    }

    [Fact]
    public async Task TreatsACandidateThatDisappearsBeforeItsEntryLockAsANormalRace()
    {
        TemporaryCacheFixture? observedFixture = null;
        bool removedCandidate = false;
        var logger = new RecordingLogger<DiskPreviewCache>();
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            checkpoint =>
            {
                if (checkpoint == PreviewCacheCheckpoint.CleanupCandidateCaptured && !removedCandidate)
                {
                    removedCandidate = true;
                    TemporaryCacheFixture activeFixture = Assert.IsType<TemporaryCacheFixture>(observedFixture);
                    File.Delete(activeFixture.FinalPath);
                }
            },
            TimeProvider.System,
            logger);
        observedFixture = fixture;
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.FinalPath)!);
        await File.WriteAllBytesAsync(fixture.FinalPath, [1], CancellationToken.None);

        await fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
            .WaitAsync(coordinationTimeout);

        Assert.False(File.Exists(fixture.FinalPath));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
        RecordedLog summary = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Equal(0, summary.Properties["DeletedFiles"]);
        Assert.Equal(0, summary.Properties["FailedFiles"]);
        Assert.Equal(0, summary.Properties["SkippedChangedFiles"]);
    }

    [Fact]
    public async Task ContinuesAfterAnEnumeratedEntryDisappearsBeforeInspection()
    {
        TemporaryCacheFixture? observedFixture = null;
        bool removedEntry = false;
        var logger = new RecordingLogger<DiskPreviewCache>();
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            checkpoint =>
            {
                if (checkpoint == PreviewCacheCheckpoint.CleanupEntryDiscovered && !removedEntry)
                {
                    removedEntry = true;
                    TemporaryCacheFixture activeFixture = Assert.IsType<TemporaryCacheFixture>(observedFixture);
                    File.Delete(activeFixture.FinalPath);
                }
            },
            TimeProvider.System,
            logger);
        observedFixture = fixture;
        string directoryPath = Path.GetDirectoryName(fixture.FinalPath)!;
        Directory.CreateDirectory(directoryPath);
        string siblingPath = Path.Combine(directoryPath, "f0000000001.jpg");
        await File.WriteAllBytesAsync(fixture.FinalPath, [1], CancellationToken.None);
        await File.WriteAllBytesAsync(siblingPath, [2], CancellationToken.None);

        await fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
            .WaitAsync(coordinationTimeout);

        Assert.False(File.Exists(siblingPath));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task SerializesOverlappingCleanupRuns()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requestedRuns = 0;
        int startedRuns = 0;
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(checkpoint =>
        {
            if (checkpoint == PreviewCacheCheckpoint.CleanupRunRequested
                && Interlocked.Increment(ref requestedRuns) == 2)
            {
                secondRequested.SetResult();
            }

            if (checkpoint != PreviewCacheCheckpoint.CleanupStarted)
            {
                return;
            }

            int runNumber = Interlocked.Increment(ref startedRuns);
            if (runNumber == 1)
            {
                firstStarted.SetResult();
                releaseFirst.Task.GetAwaiter().GetResult();
            }
        });

        Task first = Task.Run(() => fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None));
        await firstStarted.Task.WaitAsync(coordinationTimeout);
        Task second = Task.Run(() => fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None));
        await secondRequested.Task.WaitAsync(coordinationTimeout);

        Assert.False(second.IsCompleted);
        Assert.Equal(1, Volatile.Read(ref startedRuns));
        releaseFirst.SetResult();
        await Task.WhenAll(first, second).WaitAsync(coordinationTimeout);
        Assert.Equal(2, Volatile.Read(ref startedRuns));
    }

    [Fact]
    public async Task LogsCancellationWhileWaitingForAnotherCleanupRun()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int requestedRuns = 0;
        int startedRuns = 0;
        var logger = new RecordingLogger<DiskPreviewCache>();
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            checkpoint =>
            {
                if (checkpoint == PreviewCacheCheckpoint.CleanupRunRequested
                    && Interlocked.Increment(ref requestedRuns) == 2)
                {
                    secondRequested.SetResult();
                }

                if (checkpoint == PreviewCacheCheckpoint.CleanupStarted
                    && Interlocked.Increment(ref startedRuns) == 1)
                {
                    firstStarted.SetResult();
                    releaseFirst.Task.GetAwaiter().GetResult();
                }
            },
            TimeProvider.System,
            logger);
        using var cancellation = new CancellationTokenSource();
        Task first = Task.Run(() => fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None));
        await firstStarted.Task.WaitAsync(coordinationTimeout);
        Task second = Task.Run(() => fixture.Cache.ClearAsync(new RecordingProgress(), cancellation.Token));
        await secondRequested.Task.WaitAsync(coordinationTimeout);

        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => second.WaitAsync(coordinationTimeout));
            RecordedLog cancelledSummary = Assert.Single(
                logger.Entries,
                entry => entry.Level == LogLevel.Information
                    && Equals(entry.Properties["Cancelled"], true));
            Assert.Equal(0, cancelledSummary.Properties["DeletedFiles"]);
            Assert.Equal(1, Volatile.Read(ref startedRuns));
        }
        finally
        {
            releaseFirst.TrySetResult();
            await first.WaitAsync(coordinationTimeout);
        }
    }

    [Fact]
    public async Task DoesNotTraverseANestedDirectoryReparsePoint()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string externalDirectory = Path.Combine(
            Path.GetTempPath(),
            $"trickplay-external-{Guid.NewGuid():N}");
        Directory.CreateDirectory(externalDirectory);
        string externalEntryPath = Path.Combine(externalDirectory, "f0000000000.jpg");
        await File.WriteAllBytesAsync(externalEntryPath, [1], CancellationToken.None);
        try
        {
            var logger = new RecordingLogger<DiskPreviewCache>();
            using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
                static _ => { },
                TimeProvider.System,
                logger);
            Directory.CreateDirectory(fixture.CacheRoot);
            string linkedPath = Path.Combine(fixture.CacheRoot, "linked");
            Directory.CreateSymbolicLink(linkedPath, externalDirectory);

            await fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
                .WaitAsync(coordinationTimeout);

            Assert.True(File.Exists(externalEntryPath));
            RecordedLog warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.Equal(linkedPath, warning.Properties["CachePath"]);
        }
        finally
        {
            Directory.Delete(externalDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelsBeforeTheNextFilesystemOperationAndLogsTheSummary()
    {
        using var cancellation = new CancellationTokenSource();
        var logger = new RecordingLogger<DiskPreviewCache>();
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            checkpoint =>
            {
                if (checkpoint == PreviewCacheCheckpoint.CleanupEntryLeaseAcquired)
                {
                    cancellation.Cancel();
                }
            },
            TimeProvider.System,
            logger);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.FinalPath)!);
        await File.WriteAllBytesAsync(fixture.FinalPath, [1], CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Cache.ClearAsync(new RecordingProgress(), cancellation.Token)
                .WaitAsync(coordinationTimeout));

        Assert.True(File.Exists(fixture.FinalPath));
        RecordedLog summary = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Equal(0, summary.Properties["DeletedFiles"]);
        Assert.Equal(true, summary.Properties["Cancelled"]);
    }

    [Fact]
    public async Task ContinuesAfterAnIndividualFileFailure()
    {
        bool failedFirstCandidate = false;
        var logger = new RecordingLogger<DiskPreviewCache>();
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            checkpoint =>
            {
                if (checkpoint == PreviewCacheCheckpoint.CleanupEntryLeaseAcquired && !failedFirstCandidate)
                {
                    failedFirstCandidate = true;
                    throw new IOException("Simulated deletion failure.");
                }
            },
            TimeProvider.System,
            logger);
        string directoryPath = Path.GetDirectoryName(fixture.FinalPath)!;
        Directory.CreateDirectory(directoryPath);
        string secondPath = Path.Combine(directoryPath, "f0000000001.jpg");
        await File.WriteAllBytesAsync(fixture.FinalPath, [1], CancellationToken.None);
        await File.WriteAllBytesAsync(secondPath, [2], CancellationToken.None);

        await fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
            .WaitAsync(coordinationTimeout);

        Assert.Single(new[] { fixture.FinalPath, secondPath }, File.Exists);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        RecordedLog summary = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Equal(1, summary.Properties["DeletedFiles"]);
        Assert.Equal(1, summary.Properties["FailedFiles"]);
    }

    [Fact]
    public async Task ContinuesAfterAnIndividualDirectoryFailure()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var logger = new RecordingLogger<DiskPreviewCache>();
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(
            static _ => { },
            TimeProvider.System,
            logger);
        string blockedDirectory = Path.Combine(fixture.CacheRoot, "blocked");
        Directory.CreateDirectory(blockedDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(blockedDirectory, "f0000000000.jpg"),
            [1],
            CancellationToken.None);
        string accessibleDirectory = Path.Combine(fixture.CacheRoot, "accessible");
        Directory.CreateDirectory(accessibleDirectory);
        string accessiblePath = Path.Combine(accessibleDirectory, "f0000000001.jpg");
        await File.WriteAllBytesAsync(accessiblePath, [2], CancellationToken.None);
        File.SetUnixFileMode(blockedDirectory, UnixFileMode.None);

        try
        {
            await fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None)
                .WaitAsync(coordinationTimeout);

            Assert.False(File.Exists(accessiblePath));
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == LogLevel.Warning
                    && Equals(entry.Properties["CachePath"], blockedDirectory));
            RecordedLog summary = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
            Assert.True((int)summary.Properties["FailedDirectories"]! >= 1);
        }
        finally
        {
            File.SetUnixFileMode(
                blockedDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task WaitsForAnActiveHitBeforeDeletingItsEntry()
    {
        TemporaryCacheFixture? observedFixture = null;
        Task? cleanup = null;
        var candidateCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool startedCleanup = false;
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(checkpoint =>
        {
            if (checkpoint == PreviewCacheCheckpoint.EntryLeaseAcquired && !startedCleanup)
            {
                startedCleanup = true;
                TemporaryCacheFixture activeFixture = Assert.IsType<TemporaryCacheFixture>(observedFixture);
                cleanup = Task.Run(
                    () => activeFixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None));
                Assert.True(candidateCaptured.Task.Wait(coordinationTimeout));
                Assert.False(cleanup.IsCompleted);
            }

            if (checkpoint == PreviewCacheCheckpoint.CleanupCandidateCaptured)
            {
                candidateCaptured.TrySetResult();
            }
        });
        observedFixture = fixture;
        byte[] content = [0xFF, 0xD8, 1, 0xFF, 0xD9];
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.FinalPath)!);
        await File.WriteAllBytesAsync(fixture.FinalPath, content, CancellationToken.None);

        PreviewCacheResult hit = await fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            (_, _) => throw new InvalidOperationException("An existing entry must remain a HIT."),
            CancellationToken.None).WaitAsync(coordinationTimeout);
        Task completedCleanup = Assert.IsAssignableFrom<Task>(cleanup);
        await completedCleanup.WaitAsync(coordinationTimeout);

        Assert.Equal(PreviewCacheDisposition.Hit, hit.Disposition);
        Assert.Equal(content, hit.Content.ToArray());
        Assert.False(File.Exists(fixture.FinalPath));
    }

    [Fact]
    public async Task WaitsForAnActiveMissAndLeavesItsPublicationIntact()
    {
        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var candidateCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using TemporaryCacheFixture fixture = TemporaryCacheFixture.Create(checkpoint =>
        {
            if (checkpoint == PreviewCacheCheckpoint.CleanupCandidateCaptured)
            {
                candidateCaptured.TrySetResult();
            }
        });
        byte[] content = [0xFF, 0xD8, 2, 0xFF, 0xD9];
        Task<PreviewCacheResult> miss = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                writerStarted.SetResult();
                await releaseWriter.Task.WaitAsync(cancellationToken);
                await destination.WriteAsync(content, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.Zero, TimeSpan.Zero);
            },
            CancellationToken.None);
        await writerStarted.Task.WaitAsync(coordinationTimeout);

        Task cleanup = Task.Run(
            () => fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None));
        await candidateCaptured.Task.WaitAsync(coordinationTimeout);
        Assert.False(cleanup.IsCompleted);
        releaseWriter.SetResult();
        PreviewCacheResult result = await miss.WaitAsync(coordinationTimeout);
        await cleanup.WaitAsync(coordinationTimeout);

        Assert.Equal(PreviewCacheDisposition.Miss, result.Disposition);
        Assert.Equal(content, await File.ReadAllBytesAsync(fixture.FinalPath, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
    }

    private static IApplicationPaths CreateApplicationPaths(string temporaryDirectory)
    {
        IApplicationPaths paths = DispatchProxy.Create<IApplicationPaths, ApplicationPathsSpecs>();
        ((ApplicationPathsSpecs)(object)paths).TemporaryDirectory = temporaryDirectory;
        return paths;
    }

    private static PreviewIdentity CreateIdentity()
    {
        return CreateIdentity("f0000000000.jpg");
    }

    private static PreviewIdentity CreateIdentity(string entryName)
    {
        return new PreviewIdentity(
            "0123456789abcdef0123456789abcdef",
            "\"0123456789abcdef0123456789abcdef-f0000000000\"",
            Path.Combine(
                "3f728b7b4aa54f65b488a6029edb6725",
                "w0320",
                "s000000-0123456789abcdef0123456789abcdef",
                entryName));
    }

    public class ApplicationPathsSpecs : DispatchProxy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationPathsSpecs"/> class.
        /// </summary>
        public ApplicationPathsSpecs()
        {
        }

        public string TemporaryDirectory { get; set; } = string.Empty;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            return targetMethod.Name == "get_TempDirectory"
                ? TemporaryDirectory
                : throw new InvalidOperationException($"Unexpected application-paths call: {targetMethod.Name}.");
        }
    }

    public class ServerApplicationHostSpecs : DispatchProxy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerApplicationHostSpecs"/> class.
        /// </summary>
        public ServerApplicationHostSpecs()
        {
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new InvalidOperationException($"Unexpected application-host call: {targetMethod?.Name}.");
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            this.utcNow = utcNow;
        }

        public int GetUtcNowCallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCallCount++;
            return utcNow;
        }
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        private readonly List<double> values = [];

        public IReadOnlyList<double> Values => values;

        public void Report(double value)
        {
            values.Add(value);
        }
    }

    private sealed class CallbackProgress(Action<double> callback) : IProgress<double>
    {
        public void Report(double value)
        {
            callback(value);
        }
    }

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        private readonly List<RecordedLog> entries = [];

        public IReadOnlyList<RecordedLog> Entries
        {
            get
            {
                lock (entries)
                {
                    return entries.ToArray();
                }
            }
        }

        IDisposable? ILogger.BeginScope<TState>(TState state)
        {
            return null;
        }

        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Dictionary<string, object?> properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : [];
            lock (entries)
            {
                entries.Add(new RecordedLog(logLevel, exception, properties));
            }
        }
    }

    private sealed record RecordedLog(
        LogLevel Level,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class TemporaryCacheFixture : IDisposable
    {
        private readonly string temporaryDirectory;

        private TemporaryCacheFixture(
            string temporaryDirectory,
            PreviewIdentity identity,
            PreviewCacheCoordination coordination,
            TimeProvider timeProvider,
            ILogger<DiskPreviewCache> logger)
        {
            this.temporaryDirectory = temporaryDirectory;
            Identity = identity;
            FinalPath = Path.Combine(
                temporaryDirectory,
                "Jellyfin.Plugin.TrickplayCropper",
                "preview-v1",
                identity.RelativePath);
            Coordination = coordination;
            Cache = new DiskPreviewCache(
                CreateApplicationPaths(temporaryDirectory),
                timeProvider,
                coordination,
                logger);
        }

        public DiskPreviewCache Cache { get; }

        public string CacheRoot => Path.Combine(
            temporaryDirectory,
            "Jellyfin.Plugin.TrickplayCropper",
            PreviewIdentity.CacheNamespace);

        public string PluginRoot => Path.Combine(
            temporaryDirectory,
            "Jellyfin.Plugin.TrickplayCropper");

        public string FinalPath { get; }

        public PreviewIdentity Identity { get; }

        public PreviewCacheCoordination Coordination { get; }

        public static TemporaryCacheFixture Create()
        {
            return Create(static _ => { });
        }

        public static TemporaryCacheFixture Create(
            Action<PreviewCacheCheckpoint> checkpointObserver)
        {
            return Create(
                checkpointObserver,
                TimeProvider.System,
                NullLogger<DiskPreviewCache>.Instance);
        }

        public static TemporaryCacheFixture Create(
            Action<PreviewCacheCheckpoint> checkpointObserver,
            TimeProvider timeProvider,
            ILogger<DiskPreviewCache> logger)
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"trickplay-cache-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            PreviewIdentity identity = CreateIdentity();
            var coordination = new PreviewCacheCoordination(checkpointObserver);
            return new TemporaryCacheFixture(
                temporaryDirectory,
                identity,
                coordination,
                timeProvider,
                logger);
        }

        public void Dispose()
        {
            Cache.Dispose();
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
