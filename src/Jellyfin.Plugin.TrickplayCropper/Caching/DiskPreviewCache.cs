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
    private readonly string pluginRoot;
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
        pluginRoot = Path.GetFullPath(
            Path.Combine(applicationPaths.TempDirectory, PluginDirectoryName));
        cacheRoot = Path.Combine(pluginRoot, PreviewIdentity.CacheNamespace);
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
        var context = new CleanupRunContext(cleanupStartedUtc, counters, cancellationToken);
        if (!IsCleanupRootSafe(context))
        {
            progress.Report(100);
            return;
        }

        await DeleteCandidatesAsync(context).ConfigureAwait(false);
        await PruneDirectoryAsync(cacheRoot, context).ConfigureAwait(false);
        progress.Report(100);
    }

    private async Task DeleteCandidatesAsync(CleanupRunContext context)
    {
        try
        {
            var root = new DirectoryInfo(cacheRoot);
            root.Refresh();
            if (!root.Exists)
            {
                return;
            }
        }
        catch (IOException exception)
        {
            RecordDirectoryFailure(cacheRoot, exception, context.Counters);
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordDirectoryFailure(cacheRoot, exception, context.Counters);
            return;
        }
        catch (SecurityException exception)
        {
            RecordDirectoryFailure(cacheRoot, exception, context.Counters);
            return;
        }

        await DeleteDirectoryCandidatesAsync(
            cacheRoot,
            context).ConfigureAwait(false);
    }

    private async Task DeleteDirectoryCandidatesAsync(
        string directoryPath,
        CleanupRunContext context)
    {
        try
        {
            if (IsReparsePoint(directoryPath))
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
            CleanupCandidate? candidate = CaptureCandidate(filePath, context.CleanupStartedUtc);
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

    private static CleanupCandidate? CaptureCandidate(string filePath, DateTime cleanupStartedUtc)
    {
        string canonicalPath = Path.GetFullPath(filePath);
        string fileName = Path.GetFileName(canonicalPath);
        string? lockPath = null;
        bool requiresExclusiveLease = false;
        if (IsFinalEntryName(fileName))
        {
            lockPath = canonicalPath;
        }
        else if (TryGetTemporaryFinalName(fileName, out string? finalName))
        {
            lockPath = Path.Combine(Path.GetDirectoryName(canonicalPath)!, finalName!);
        }
        else if (fileName.EndsWith(TemporaryExtension, StringComparison.Ordinal))
        {
            requiresExclusiveLease = true;
        }
        else
        {
            return null;
        }

        var file = new FileInfo(canonicalPath);
        file.Refresh();
        if (!file.Exists || file.LastWriteTimeUtc > cleanupStartedUtc)
        {
            return null;
        }

        return requiresExclusiveLease
            ? new ExclusiveCleanupCandidate(canonicalPath, file.Length, file.LastWriteTimeUtc.Ticks)
            : new EntryCleanupCandidate(
                canonicalPath,
                lockPath ?? throw new UnreachableException("An entry cleanup candidate requires a lock path."),
                file.Length,
                file.LastWriteTimeUtc.Ticks);
    }

    private void DeleteOwnedCandidate(CleanupCandidate candidate, CleanupRunContext context)
    {
        if (IsReparsePoint(candidate.Path))
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
            if (IsReparsePoint(directoryPath))
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
            if (IsReparsePoint(directoryPath))
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
        LogDirectoryFailure(logger, directoryPath, exception);
    }

    private void WarnAboutReparsePoint(string path, CleanupRunContext context)
    {
        if (context.TryRecordReparsePoint(path))
        {
            LogReparsePointSkipped(logger, path);
        }
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
        byte[]? existingContent = await TryReadSafeExistingAsync(finalPath, cancellationToken)
            .ConfigureAwait(false);
        if (existingContent is not null)
        {
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
            EnsureRequestPathIsSafe(temporaryPath);
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
            DeleteTemporaryEntryIfSafe(temporaryPath);
        }
    }

    private async Task<(ReadOnlyMemory<byte> Content, PreviewCacheDisposition Disposition)> PublishAsync(
        string finalPath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        byte[]? existingContent = await TryReadSafeExistingAsync(finalPath, cancellationToken)
            .ConfigureAwait(false);
        if (existingContent is not null)
        {
            return (existingContent, PreviewCacheDisposition.Hit);
        }

        try
        {
            PublishTemporaryEntry(finalPath, temporaryPath, cancellationToken);
        }
        catch (IOException exception)
        {
            return await ReadWinningPublicationAsync(finalPath, exception, cancellationToken).ConfigureAwait(false);
        }

        byte[] content = await ReadPublishedEntryAsync(finalPath, cancellationToken).ConfigureAwait(false);
        return (content, PreviewCacheDisposition.Miss);
    }

    private void PublishTemporaryEntry(
        string finalPath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        coordination.ObserveBeforePublication();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRequestPathIsSafe(finalPath);
        EnsureRequestPathIsSafe(temporaryPath);
        File.Move(temporaryPath, finalPath, overwrite: false);
    }

    private async Task<byte[]> ReadPublishedEntryAsync(
        string finalPath,
        CancellationToken cancellationToken)
    {
        coordination.ObserveAfterPublication();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRequestPathIsSafe(finalPath);
        byte[] content = await File.ReadAllBytesAsync(finalPath, cancellationToken).ConfigureAwait(false);
        EnsureRequestPathIsSafe(finalPath);
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }

    private async Task<(ReadOnlyMemory<byte> Content, PreviewCacheDisposition Disposition)>
        ReadWinningPublicationAsync(
            string finalPath,
            IOException publicationFailure,
            CancellationToken cancellationToken)
    {
        byte[]? winningContent = await TryReadSafeExistingAsync(finalPath, cancellationToken)
            .ConfigureAwait(false);
        if (winningContent is null)
        {
            ExceptionDispatchInfo.Throw(publicationFailure);
        }

        return (winningContent, PreviewCacheDisposition.Hit);
    }

    private async Task<byte[]?> TryReadSafeExistingAsync(
        string finalPath,
        CancellationToken cancellationToken)
    {
        EnsureRequestPathIsSafe(finalPath);
        byte[]? content = await TryReadExistingAsync(finalPath, cancellationToken).ConfigureAwait(false);
        if (content is not null)
        {
            EnsureRequestPathIsSafe(finalPath);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return content;
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

    private void DeleteTemporaryEntryIfSafe(string temporaryPath)
    {
        try
        {
            EnsureRequestPathIsSafe(temporaryPath);
            File.Delete(temporaryPath);
        }
        catch (InvalidDataException)
        {
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
        ThrowIfReparsePoint(pluginRoot);
        string relativePath = Path.GetRelativePath(pluginRoot, finalPath);
        string currentPath = pluginRoot;
        foreach (string segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            ThrowIfReparsePoint(currentPath);
        }
    }

    private bool IsCleanupRootSafe(CleanupRunContext context)
    {
        return IsCleanupPathSafe(pluginRoot, context)
            && IsCleanupPathSafe(cacheRoot, context);
    }

    private bool IsCleanupPathSafe(string path, CleanupRunContext context)
    {
        try
        {
            if (IsReparsePoint(path))
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

    private static void ThrowIfReparsePoint(string path)
    {
        FileAttributes? attributes = GetExistingAttributes(path);
        if (attributes.HasValue
            && (attributes.Value & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"The Cache Tree path is a reparse point: {path}");
        }
    }

    private static bool IsReparsePoint(string path)
    {
        FileAttributes? attributes = GetExistingAttributes(path);
        return attributes.HasValue
            && (attributes.Value & FileAttributes.ReparsePoint) != 0;
    }

    private static FileAttributes? GetExistingAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
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

    private abstract record CleanupCandidate(
        string Path,
        long Length,
        long LastWriteTimeUtcTicks);

    private sealed record EntryCleanupCandidate(
        string Path,
        string LockPath,
        long Length,
        long LastWriteTimeUtcTicks)
        : CleanupCandidate(Path, Length, LastWriteTimeUtcTicks);

    private sealed record ExclusiveCleanupCandidate(
        string Path,
        long Length,
        long LastWriteTimeUtcTicks)
        : CleanupCandidate(Path, Length, LastWriteTimeUtcTicks);

    private sealed class CleanupRunContext
    {
        private readonly HashSet<string> warnedReparsePoints = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        public CleanupRunContext(
            DateTime cleanupStartedUtc,
            CleanupCounters counters,
            CancellationToken cancellationToken)
        {
            CleanupStartedUtc = cleanupStartedUtc;
            Counters = counters;
            CancellationToken = cancellationToken;
        }

        public CancellationToken CancellationToken { get; }

        public DateTime CleanupStartedUtc { get; }

        public CleanupCounters Counters { get; }

        public bool TryRecordReparsePoint(string path)
        {
            return warnedReparsePoints.Add(path);
        }
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
