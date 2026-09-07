using System.Diagnostics;
using System.Security;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Removes expired Preview Cache Entries while preserving cache ownership boundaries.
/// </summary>
internal sealed class DiskPreviewCacheCleanup : IDisposable
{
    private readonly SemaphoreSlim cleanupMutex = new(1, 1);
    private readonly PreviewCacheCoordination coordination;
    private readonly ILogger logger;
    private readonly PreviewCachePaths paths;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskPreviewCacheCleanup"/> class.
    /// </summary>
    public DiskPreviewCacheCleanup(
        PreviewCachePaths paths,
        TimeProvider timeProvider,
        PreviewCacheCoordination coordination,
        ILogger logger)
    {
        this.paths = paths;
        this.coordination = coordination;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task ClearAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        long cleanupRequested = Stopwatch.GetTimestamp();
        var counters = new CleanupCounters();
        try
        {
            coordination.ObserveCleanupRunRequested();
            await cleanupMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ExecuteCleanupRunAsync(progress, counters, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                cleanupMutex.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            counters.Cancelled = true;
            throw;
        }
        finally
        {
            WriteCleanupSummary(counters, Stopwatch.GetElapsedTime(cleanupRequested));
        }
    }

    private async Task ExecuteCleanupRunAsync(
        IProgress<double> progress,
        CleanupCounters counters,
        CancellationToken cancellationToken)
    {
        DateTime cleanupStartedUtc = timeProvider.GetUtcNow().UtcDateTime;
        coordination.ObserveCleanupStarted();
        progress.Report(0);
        var context = new CleanupRunContext(cleanupStartedUtc, counters, cancellationToken);
        if (!IsCleanupRootSafe(context))
        {
            progress.Report(100);
            return;
        }

        await DeleteCandidatesAsync(context).ConfigureAwait(false);
        await PruneDirectoryAsync(paths.CacheRoot, context).ConfigureAwait(false);
        progress.Report(100);
    }

    private async Task DeleteCandidatesAsync(CleanupRunContext context)
    {
        try
        {
            var root = new DirectoryInfo(paths.CacheRoot);
            root.Refresh();
            if (!root.Exists)
            {
                return;
            }
        }
        catch (IOException exception)
        {
            RecordDirectoryFailure(paths.CacheRoot, exception, context.Counters);
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordDirectoryFailure(paths.CacheRoot, exception, context.Counters);
            return;
        }
        catch (SecurityException exception)
        {
            RecordDirectoryFailure(paths.CacheRoot, exception, context.Counters);
            return;
        }

        await DeleteDirectoryCandidatesAsync(
            paths.CacheRoot,
            context).ConfigureAwait(false);
    }

    private async Task DeleteDirectoryCandidatesAsync(
        string directoryPath,
        CleanupRunContext context)
    {
        try
        {
            if (PreviewCachePaths.IsReparsePoint(directoryPath))
            {
                WarnAboutReparsePoint(directoryPath, context);
                return;
            }

            foreach (string path in Directory.EnumerateFileSystemEntries(directoryPath))
            {
                await ProcessCleanupEntryAsync(path, context).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, context.Counters);
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, context.Counters);
        }
        catch (SecurityException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, context.Counters);
        }
    }

    private async Task ProcessCleanupEntryAsync(string path, CleanupRunContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        coordination.ObserveCleanupEntryDiscovered();
        if (!TryGetCleanupEntryAttributes(path, context, out FileAttributes attributes))
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            WarnAboutReparsePoint(path, context);
        }
        else if ((attributes & FileAttributes.Directory) != 0)
        {
            await DeleteDirectoryCandidatesAsync(path, context).ConfigureAwait(false);
        }
        else
        {
            await TryDeleteCandidateAsync(path, context).ConfigureAwait(false);
        }
    }

    private bool TryGetCleanupEntryAttributes(
        string path,
        CleanupRunContext context,
        out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException exception)
        {
            RecordFileFailure(path, exception, context.Counters);
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordFileFailure(path, exception, context.Counters);
        }
        catch (SecurityException exception)
        {
            RecordFileFailure(path, exception, context.Counters);
        }

        attributes = default;
        return false;
    }

    private async Task TryDeleteCandidateAsync(string filePath, CleanupRunContext context)
    {
        try
        {
            CleanupCandidate? candidate = CleanupCandidate.Capture(filePath, context.CleanupStartedUtc);
            if (candidate is null)
            {
                return;
            }

            coordination.ObserveCleanupCandidateCaptured();
            if (candidate is ExclusiveCleanupCandidate exclusiveCandidate)
            {
                using IDisposable treeLease = await coordination
                    .AcquireExclusiveAsync(context.CancellationToken)
                    .ConfigureAwait(false);
                context.CancellationToken.ThrowIfCancellationRequested();
                DeleteOwnedCandidate(exclusiveCandidate, context);
                return;
            }

            if (candidate is not EntryCleanupCandidate entryCandidate)
            {
                throw new UnreachableException("Unknown cleanup candidate type.");
            }

            await coordination.ExecuteCleanupEntryAsync(
                entryCandidate.LockPath,
                () => DeleteOwnedCandidate(entryCandidate, context),
                context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException exception)
        {
            RecordFileFailure(filePath, exception, context.Counters);
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordFileFailure(filePath, exception, context.Counters);
        }
        catch (SecurityException exception)
        {
            RecordFileFailure(filePath, exception, context.Counters);
        }
    }

    private void DeleteOwnedCandidate(CleanupCandidate candidate, CleanupRunContext context)
    {
        if (PreviewCachePaths.IsReparsePoint(candidate.Path))
        {
            WarnAboutReparsePoint(candidate.Path, context);
            return;
        }

        var current = new FileInfo(candidate.Path);
        current.Refresh();
        if (!current.Exists)
        {
            return;
        }

        if (current.Length != candidate.Length || current.LastWriteTimeUtc.Ticks != candidate.LastWriteTimeUtcTicks)
        {
            context.Counters.SkippedChangedFiles++;
            return;
        }

        File.Delete(candidate.Path);
        context.Counters.DeletedFiles++;
    }

    private void RecordFileFailure(string filePath, Exception exception, CleanupCounters counters)
    {
        counters.FailedFiles++;
        DiskPreviewCacheCleanupLog.FileFailure(logger, filePath, exception);
    }

    private async Task PruneDirectoryAsync(
        string directoryPath,
        CleanupRunContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (!await PruneChildDirectoriesAsync(directoryPath, context).ConfigureAwait(false))
        {
            return;
        }

        await TryDeleteEmptyDirectoryAsync(directoryPath, context).ConfigureAwait(false);
    }

    private async Task<bool> PruneChildDirectoriesAsync(
        string directoryPath,
        CleanupRunContext context)
    {
        try
        {
            if (PreviewCachePaths.IsReparsePoint(directoryPath))
            {
                WarnAboutReparsePoint(directoryPath, context);
                return false;
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(directoryPath))
            {
                await PruneDirectoryAsync(childDirectory, context).ConfigureAwait(false);
            }

            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (IOException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, context.Counters);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, context.Counters);
            return false;
        }
        catch (SecurityException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, context.Counters);
            return false;
        }
    }

    private async Task TryDeleteEmptyDirectoryAsync(string directoryPath, CleanupRunContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        try
        {
            using IDisposable treeLease = await coordination
                .AcquireExclusiveAsync(context.CancellationToken)
                .ConfigureAwait(false);
            context.CancellationToken.ThrowIfCancellationRequested();
            if (PreviewCachePaths.IsReparsePoint(directoryPath))
            {
                WarnAboutReparsePoint(directoryPath, context);
                return;
            }

            if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath);
                context.Counters.DeletedDirectories++;
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, context.Counters);
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, context.Counters);
        }
        catch (SecurityException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, context.Counters);
        }
    }

    private void RecordDirectoryFailure(string directoryPath, Exception exception, CleanupCounters counters)
    {
        counters.FailedDirectories++;
        DiskPreviewCacheCleanupLog.DirectoryFailure(logger, directoryPath, exception);
    }

    private void WarnAboutReparsePoint(string path, CleanupRunContext context)
    {
        if (context.TryRecordReparsePoint(path))
        {
            DiskPreviewCacheCleanupLog.ReparsePointSkipped(logger, path);
        }
    }

    private void WriteCleanupSummary(CleanupCounters counters, TimeSpan elapsed)
    {
        DiskPreviewCacheCleanupLog.Summary(
            logger,
            counters.DeletedFiles,
            counters.DeletedDirectories,
            counters.FailedFiles,
            counters.FailedDirectories,
            counters.SkippedChangedFiles,
            checked((long)elapsed.TotalMilliseconds),
            counters.Cancelled);
    }

    private bool IsCleanupRootSafe(CleanupRunContext context)
    {
        return IsCleanupPathSafe(paths.PluginRoot, context)
            && IsCleanupPathSafe(paths.CacheRoot, context);
    }

    private bool IsCleanupPathSafe(string path, CleanupRunContext context)
    {
        try
        {
            if (PreviewCachePaths.IsReparsePoint(path))
            {
                WarnAboutReparsePoint(path, context);
                return false;
            }

            return true;
        }
        catch (IOException exception)
        {
            RecordDirectoryFailure(path, exception, context.Counters);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordDirectoryFailure(path, exception, context.Counters);
            return false;
        }
        catch (SecurityException exception)
        {
            RecordDirectoryFailure(path, exception, context.Counters);
            return false;
        }
    }

    /// <summary>
    /// Releases the cleanup-run mutex owned by this process-wide cache instance.
    /// </summary>
    public void Dispose()
    {
        cleanupMutex.Dispose();
    }

}
