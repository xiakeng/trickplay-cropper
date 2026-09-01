using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Stores Preview Cache Entries beneath Jellyfin temporary storage.
/// </summary>
internal sealed partial class DiskPreviewCache : IPreviewCache, IDisposable
{
    private const string PluginDirectoryName = "Jellyfin.Plugin.TrickplayCropper";
    private const string FinalExtension = ".jpg";
    private const string TemporaryExtension = ".tmp";
    private const int FrameDigits = 10;
    private const int TemporaryTokenLength = 32;

    private readonly string cacheRoot;
    private readonly SemaphoreSlim cleanupMutex = new(1, 1);
    private readonly PreviewCacheCoordination coordination;
    private readonly ILogger<DiskPreviewCache> logger;
    private readonly StringComparison pathComparison;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskPreviewCache"/> class.
    /// </summary>
    public DiskPreviewCache(
        IApplicationPaths applicationPaths,
        TimeProvider timeProvider,
        ILogger<DiskPreviewCache> logger)
        : this(
            applicationPaths,
            timeProvider,
            new PreviewCacheCoordination(),
            logger)
    {
    }

    /// <summary>
    /// Initializes a cache with an explicit process-local coordination collaborator.
    /// </summary>
    /// <param name="applicationPaths">The Jellyfin application paths.</param>
    /// <param name="timeProvider">The source of cleanup time.</param>
    /// <param name="coordination">Coordinates Cache Tree ownership and deterministic boundaries.</param>
    internal DiskPreviewCache(
        IApplicationPaths applicationPaths,
        TimeProvider timeProvider,
        PreviewCacheCoordination coordination,
        ILogger<DiskPreviewCache> logger)
    {
        cacheRoot = Path.GetFullPath(
            Path.Combine(applicationPaths.TempDirectory, PluginDirectoryName, PreviewIdentity.CacheNamespace));
        this.coordination = coordination;
        this.logger = logger;
        pathComparison = coordination.PathComparison;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<PreviewCacheResult> GetOrCreateAsync(
        PreviewIdentity identity,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string finalPath = GetFinalPath(identity);
        return await coordination.ExecuteEntryAsync(
            finalPath,
            token => GetOrCreateOwnedAsync(finalPath, writer, token),
            cancellationToken).ConfigureAwait(false);
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
        await DeleteCandidatesAsync(cleanupStartedUtc, counters, cancellationToken).ConfigureAwait(false);
        if (!IsReparsePoint(cacheRoot))
        {
            await PruneDirectoryAsync(cacheRoot, counters, cancellationToken).ConfigureAwait(false);
        }

        progress.Report(100);
    }

    private async Task DeleteCandidatesAsync(
        DateTime cleanupStartedUtc,
        CleanupCounters counters,
        CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(cacheRoot);
        root.Refresh();
        if (!root.Exists)
        {
            return;
        }

        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            LogReparsePointSkipped(logger, cacheRoot);
            return;
        }

        await DeleteDirectoryCandidatesAsync(
            cacheRoot,
            cleanupStartedUtc,
            counters,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteDirectoryCandidatesAsync(
        string directoryPath,
        DateTime cleanupStartedUtc,
        CleanupCounters counters,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directoryPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    LogReparsePointSkipped(logger, path);
                }
                else if ((attributes & FileAttributes.Directory) != 0)
                {
                    await DeleteDirectoryCandidatesAsync(
                        path,
                        cleanupStartedUtc,
                        counters,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await TryDeleteCandidateAsync(path, cleanupStartedUtc, counters, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, counters);
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, counters);
        }
        catch (SecurityException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, counters);
        }
    }

    private async Task TryDeleteCandidateAsync(
        string filePath,
        DateTime cleanupStartedUtc,
        CleanupCounters counters,
        CancellationToken cancellationToken)
    {
        try
        {
            CleanupCandidate? candidate = CaptureCandidate(filePath, cleanupStartedUtc);
            if (candidate is null)
            {
                return;
            }

            coordination.ObserveCleanupCandidateCaptured();
            if (candidate.Kind == CleanupFileKind.UnparseableTemporary)
            {
                using IDisposable treeLease = await coordination
                    .AcquireExclusiveAsync(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                DeleteOwnedCandidate(candidate, counters);
                return;
            }

            await coordination.ExecuteCleanupEntryAsync(
                candidate.LockPath!,
                () => DeleteOwnedCandidate(candidate, counters),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException exception)
        {
            RecordFileFailure(filePath, exception, counters);
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordFileFailure(filePath, exception, counters);
        }
        catch (SecurityException exception)
        {
            RecordFileFailure(filePath, exception, counters);
        }
    }

    private static CleanupCandidate? CaptureCandidate(string filePath, DateTime cleanupStartedUtc)
    {
        string canonicalPath = Path.GetFullPath(filePath);
        string fileName = Path.GetFileName(canonicalPath);
        (CleanupFileKind Kind, string? LockPath)? cleanupIdentity = GetCleanupIdentity(canonicalPath, fileName);
        if (cleanupIdentity is null)
        {
            return null;
        }

        var file = new FileInfo(canonicalPath);
        file.Refresh();
        if (!file.Exists || file.LastWriteTimeUtc > cleanupStartedUtc)
        {
            return null;
        }

        return new CleanupCandidate(
            canonicalPath,
            cleanupIdentity.Value.LockPath,
            cleanupIdentity.Value.Kind,
            file.Length,
            file.LastWriteTimeUtc.Ticks);
    }

    private void DeleteOwnedCandidate(CleanupCandidate candidate, CleanupCounters counters)
    {
        if (IsReparsePoint(candidate.Path))
        {
            LogReparsePointSkipped(logger, candidate.Path);
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
            counters.SkippedChangedFiles++;
            return;
        }

        File.Delete(candidate.Path);
        counters.DeletedFiles++;
    }

    private static (CleanupFileKind Kind, string? LockPath)? GetCleanupIdentity(
        string canonicalPath,
        string fileName)
    {
        if (IsFinalEntryName(fileName))
        {
            return (CleanupFileKind.FinalJpeg, canonicalPath);
        }

        if (!TryGetTemporaryFinalName(fileName, out string? finalName))
        {
            return fileName.EndsWith(TemporaryExtension, StringComparison.Ordinal)
                ? (CleanupFileKind.UnparseableTemporary, null)
                : null;
        }

        string lockPath = Path.Combine(Path.GetDirectoryName(canonicalPath)!, finalName!);
        return (CleanupFileKind.Temporary, lockPath);
    }

    private static bool IsFinalEntryName(string fileName)
    {
        if (fileName.Length != 1 + FrameDigits + FinalExtension.Length
            || fileName[0] != 'f'
            || !fileName.EndsWith(FinalExtension, StringComparison.Ordinal))
        {
            return false;
        }

        return fileName.AsSpan(1, FrameDigits).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    private static bool TryGetTemporaryFinalName(string fileName, out string? finalName)
    {
        finalName = null;
        if (!fileName.EndsWith(TemporaryExtension, StringComparison.Ordinal))
        {
            return false;
        }

        string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        int separatorIndex = withoutExtension.LastIndexOf('.');
        if (separatorIndex < 0)
        {
            return false;
        }

        string entryName = withoutExtension[..separatorIndex];
        string token = withoutExtension[(separatorIndex + 1)..];
        string candidateFinalName = string.Concat(entryName, FinalExtension);
        if (!IsFinalEntryName(candidateFinalName)
            || token.Length != TemporaryTokenLength
            || !Guid.TryParseExact(token, "N", out _)
            || !string.Equals(token, token.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return false;
        }

        finalName = candidateFinalName;
        return true;
    }

    private void RecordFileFailure(string filePath, Exception exception, CleanupCounters counters)
    {
        counters.FailedFiles++;
        LogFileFailure(logger, filePath, exception);
    }

    private async Task PruneDirectoryAsync(
        string directoryPath,
        CleanupCounters counters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<string> childDirectories;
        try
        {
            childDirectories = Directory.EnumerateDirectories(
                directoryPath,
                "*",
                new EnumerationOptions
                {
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = false,
                    RecurseSubdirectories = false,
                    ReturnSpecialDirectories = false,
                });
            foreach (string childDirectory in childDirectories)
            {
                await PruneDirectoryAsync(childDirectory, counters, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (IOException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, counters);
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, counters);
            return;
        }
        catch (SecurityException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, counters);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using IDisposable treeLease = await coordination
                .AcquireExclusiveAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath);
                counters.DeletedDirectories++;
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, counters);
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, counters);
        }
        catch (SecurityException exception)
        {
            RecordDirectoryFailure(directoryPath, exception, counters);
        }
    }

    private void RecordDirectoryFailure(string directoryPath, Exception exception, CleanupCounters counters)
    {
        counters.FailedDirectories++;
        LogDirectoryFailure(logger, directoryPath, exception);
    }

    private void WriteCleanupSummary(CleanupCounters counters, TimeSpan elapsed)
    {
        LogCleanupSummary(
            logger,
            counters.DeletedFiles,
            counters.DeletedDirectories,
            counters.FailedFiles,
            counters.FailedDirectories,
            counters.SkippedChangedFiles,
            checked((long)elapsed.TotalMilliseconds),
            counters.Cancelled);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Failed to delete Trickplay Cropper cache file {CachePath}.")]
    private static partial void LogFileFailure(
        ILogger logger,
        string cachePath,
        Exception exception);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Failed to inspect or delete Trickplay Cropper cache directory {CachePath}.")]
    private static partial void LogDirectoryFailure(
        ILogger logger,
        string cachePath,
        Exception exception);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Skipped Trickplay Cropper cache reparse point {CachePath}.")]
    private static partial void LogReparsePointSkipped(ILogger logger, string cachePath);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Trickplay Cropper cache cleanup completed. DeletedFiles={DeletedFiles} "
            + "DeletedDirectories={DeletedDirectories} FailedFiles={FailedFiles} "
            + "FailedDirectories={FailedDirectories} SkippedChangedFiles={SkippedChangedFiles} "
            + "ElapsedMilliseconds={ElapsedMilliseconds} Cancelled={Cancelled}")]
    private static partial void LogCleanupSummary(
        ILogger logger,
        int deletedFiles,
        int deletedDirectories,
        int failedFiles,
        int failedDirectories,
        int skippedChangedFiles,
        long elapsedMilliseconds,
        bool cancelled);

    private async Task<PreviewCacheResult> GetOrCreateOwnedAsync(
        string finalPath,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        EnsureRequestPathIsSafe(finalPath);
        byte[]? existingContent = await TryReadExistingAsync(finalPath, cancellationToken).ConfigureAwait(false);
        if (existingContent is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PreviewCacheResult(existingContent, PreviewCacheDisposition.Hit, null);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        EnsureRequestPathIsSafe(finalPath);
        return await GenerateAsync(finalPath, writer, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PreviewCacheResult> GenerateAsync(
        string finalPath,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        string temporaryPath = CreateTemporaryPath(finalPath);
        try
        {
            PreviewEncodingTelemetry telemetry = await WriteTemporaryEntryAsync(
                temporaryPath,
                writer,
                cancellationToken).ConfigureAwait(false);
            ValidateCompletedOutput(temporaryPath);
            cancellationToken.ThrowIfCancellationRequested();
            (ReadOnlyMemory<byte> content, PreviewCacheDisposition disposition) = await PublishAsync(
                finalPath,
                temporaryPath,
                cancellationToken).ConfigureAwait(false);
            return new PreviewCacheResult(content, disposition, telemetry);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<(ReadOnlyMemory<byte> Content, PreviewCacheDisposition Disposition)> PublishAsync(
        string finalPath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        EnsureRequestPathIsSafe(finalPath);
        byte[]? existingContent = await TryReadExistingAsync(finalPath, cancellationToken).ConfigureAwait(false);
        if (existingContent is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (existingContent, PreviewCacheDisposition.Hit);
        }

        coordination.ObserveBeforePublication();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRequestPathIsSafe(finalPath);
        try
        {
            File.Move(temporaryPath, finalPath, overwrite: false);
        }
        catch (IOException exception)
        {
            return await ReadWinningPublicationAsync(finalPath, exception, cancellationToken).ConfigureAwait(false);
        }

        coordination.ObserveAfterPublication();
        cancellationToken.ThrowIfCancellationRequested();
        byte[] content = await File.ReadAllBytesAsync(finalPath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return (content, PreviewCacheDisposition.Miss);
    }

    private static async Task<(ReadOnlyMemory<byte> Content, PreviewCacheDisposition Disposition)>
        ReadWinningPublicationAsync(
            string finalPath,
            IOException publicationFailure,
            CancellationToken cancellationToken)
    {
        byte[]? winningContent = await TryReadExistingAsync(finalPath, cancellationToken).ConfigureAwait(false);
        if (winningContent is null)
        {
            ExceptionDispatchInfo.Throw(publicationFailure);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (winningContent, PreviewCacheDisposition.Hit);
    }

    private static async Task<byte[]?> TryReadExistingAsync(
        string finalPath,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] content = await File.ReadAllBytesAsync(finalPath, cancellationToken).ConfigureAwait(false);
            return content.Length > 0
                ? content
                : throw new InvalidDataException("The existing Preview Cache Entry is empty.");
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static async Task<PreviewEncodingTelemetry> WriteTemporaryEntryAsync(
        string temporaryPath,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        PreviewEncodingTelemetry telemetry = await writer(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return telemetry;
    }

    private static void ValidateCompletedOutput(string temporaryPath)
    {
        var output = new FileInfo(temporaryPath);
        if (!output.Exists || output.Length == 0)
        {
            throw new InvalidDataException("The generated Preview Cache Entry is empty or missing.");
        }
    }

    private string GetFinalPath(PreviewIdentity identity)
    {
        string finalPath = Path.GetFullPath(Path.Combine(cacheRoot, identity.RelativePath));
        string containedRoot = string.Concat(cacheRoot, Path.DirectorySeparatorChar);
        if (!finalPath.StartsWith(containedRoot, pathComparison))
        {
            throw new InvalidDataException("The Preview Cache Entry path escapes the Cache Tree.");
        }

        return finalPath;
    }

    private void EnsureRequestPathIsSafe(string finalPath)
    {
        ThrowIfReparsePoint(cacheRoot);
        string relativePath = Path.GetRelativePath(cacheRoot, finalPath);
        string currentPath = cacheRoot;
        foreach (string segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            ThrowIfReparsePoint(currentPath);
        }
    }

    private static void ThrowIfReparsePoint(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"The Cache Tree path is a reparse point: {path}");
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string CreateTemporaryPath(string finalPath)
    {
        string directoryPath = Path.GetDirectoryName(finalPath)!;
        string entryName = Path.GetFileNameWithoutExtension(finalPath);
        return Path.Combine(directoryPath, $"{entryName}.{Guid.NewGuid():N}.tmp");
    }

    /// <summary>
    /// Releases the cleanup-run mutex owned by this process-wide cache instance.
    /// </summary>
    public void Dispose()
    {
        cleanupMutex.Dispose();
    }

    private sealed record CleanupCandidate(
        string Path,
        string? LockPath,
        CleanupFileKind Kind,
        long Length,
        long LastWriteTimeUtcTicks);

    private enum CleanupFileKind
    {
        FinalJpeg,
        Temporary,
        UnparseableTemporary,
    }

    private sealed class CleanupCounters
    {
        public int DeletedFiles { get; set; }

        public int FailedFiles { get; set; }

        public int DeletedDirectories { get; set; }

        public int FailedDirectories { get; set; }

        public int SkippedChangedFiles { get; set; }

        public bool Cancelled { get; set; }
    }

}
