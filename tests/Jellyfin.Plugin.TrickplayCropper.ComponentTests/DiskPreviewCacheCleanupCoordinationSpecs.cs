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

public sealed class DiskPreviewCacheCleanupCoordinationSpecs
{
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
        await firstStarted.Task.WaitAsync(CoordinationTimeout);
        Task second = Task.Run(() => fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None));
        await secondRequested.Task.WaitAsync(CoordinationTimeout);

        Assert.False(second.IsCompleted);
        Assert.Equal(1, Volatile.Read(ref startedRuns));
        releaseFirst.SetResult();
        await Task.WhenAll(first, second).WaitAsync(CoordinationTimeout);
        Assert.Equal(2, Volatile.Read(ref startedRuns));
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
                Assert.True(candidateCaptured.Task.Wait(CoordinationTimeout));
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
            CancellationToken.None).WaitAsync(CoordinationTimeout);
        Task completedCleanup = Assert.IsAssignableFrom<Task>(cleanup);
        await completedCleanup.WaitAsync(CoordinationTimeout);

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
        await writerStarted.Task.WaitAsync(CoordinationTimeout);

        Task cleanup = Task.Run(
            () => fixture.Cache.ClearAsync(new RecordingProgress(), CancellationToken.None));
        await candidateCaptured.Task.WaitAsync(CoordinationTimeout);
        Assert.False(cleanup.IsCompleted);
        releaseWriter.SetResult();
        PreviewCacheResult result = await miss.WaitAsync(CoordinationTimeout);
        await cleanup.WaitAsync(CoordinationTimeout);

        Assert.Equal(PreviewCacheDisposition.Miss, result.Disposition);
        Assert.Equal(content, await File.ReadAllBytesAsync(fixture.FinalPath, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
    }

}
