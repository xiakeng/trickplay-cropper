using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Defines stable cleanup log events independently from cleanup traversal.
/// </summary>
internal static partial class DiskPreviewCacheCleanupLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Failed to delete Trickplay Cropper cache file {CachePath}.")]
    public static partial void FileFailure(
        ILogger logger,
        string cachePath,
        Exception exception);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Failed to inspect or delete Trickplay Cropper cache directory {CachePath}.")]
    public static partial void DirectoryFailure(
        ILogger logger,
        string cachePath,
        Exception exception);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Skipped Trickplay Cropper cache reparse point {CachePath}.")]
    public static partial void ReparsePointSkipped(ILogger logger, string cachePath);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Trickplay Cropper cache cleanup completed. DeletedFiles={DeletedFiles} "
            + "DeletedDirectories={DeletedDirectories} FailedFiles={FailedFiles} "
            + "FailedDirectories={FailedDirectories} SkippedChangedFiles={SkippedChangedFiles} "
            + "ElapsedMilliseconds={ElapsedMilliseconds} Cancelled={Cancelled}")]
    public static partial void Summary(
        ILogger logger,
        int deletedFiles,
        int deletedDirectories,
        int failedFiles,
        int failedDirectories,
        int skippedChangedFiles,
        long elapsedMilliseconds,
        bool cancelled);
}
