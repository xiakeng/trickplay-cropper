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

public sealed class DiskPreviewCacheCleanupEligibilitySpecs : DiskPreviewCacheSharedSpecs
{
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

        await fixture.Cache.ClearAsync(progress, CancellationToken.None).WaitAsync(CoordinationTimeout);

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
        await writerStarted.Task.WaitAsync(CoordinationTimeout);
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var progress = new CallbackProgress(value =>
        {
            if (value == 0)
            {
                cleanupStarted.TrySetResult();
            }
        });

        Task cleanup = Task.Run(() => fixture.Cache.ClearAsync(progress, CancellationToken.None));
        await cleanupStarted.Task.WaitAsync(CoordinationTimeout);

        Assert.False(cleanup.IsCompleted);
        Assert.True(File.Exists(unparseableTemporaryPath));
        releaseWriter.SetResult();
        await Task.WhenAll(request, cleanup).WaitAsync(CoordinationTimeout);
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
            .WaitAsync(CoordinationTimeout);

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
            .WaitAsync(CoordinationTimeout);

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
                .WaitAsync(CoordinationTimeout);

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
            .WaitAsync(CoordinationTimeout);

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
            .WaitAsync(CoordinationTimeout);

        Assert.False(File.Exists(siblingPath));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

}
