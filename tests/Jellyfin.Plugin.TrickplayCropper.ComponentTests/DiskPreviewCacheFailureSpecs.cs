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

public sealed class DiskPreviewCacheFailureSpecs
{
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
        await ownerStarted.Task.WaitAsync(CoordinationTimeout);
        Task<PreviewCacheResult> waiter = fixture.Cache.GetOrCreateAsync(
            fixture.Identity,
            async (destination, cancellationToken) =>
            {
                await destination.WriteAsync(generatedContent, cancellationToken);
                return new PreviewEncodingTelemetry(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2));
            },
            CancellationToken.None);

        failOwner.SetResult();

        await Assert.ThrowsAsync<IOException>(() => owner.WaitAsync(CoordinationTimeout));
        PreviewCacheResult result = await waiter.WaitAsync(CoordinationTimeout);
        Assert.Equal(PreviewCacheDisposition.Miss, result.Disposition);
        Assert.Equal(generatedContent, result.Content.ToArray());
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
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
                CancellationToken.None).WaitAsync(CoordinationTimeout));

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
                CancellationToken.None).WaitAsync(CoordinationTimeout));

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
                cancellation.Token).WaitAsync(CoordinationTimeout));

        Assert.False(File.Exists(fixture.FinalPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(fixture.FinalPath)!, "*.tmp"));
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
        await firstStarted.Task.WaitAsync(CoordinationTimeout);
        Task second = Task.Run(() => fixture.Cache.ClearAsync(new RecordingProgress(), cancellation.Token));
        await secondRequested.Task.WaitAsync(CoordinationTimeout);

        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => second.WaitAsync(CoordinationTimeout));
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
            await first.WaitAsync(CoordinationTimeout);
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
                .WaitAsync(CoordinationTimeout));

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
            .WaitAsync(CoordinationTimeout);

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
                .WaitAsync(CoordinationTimeout);

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

}
