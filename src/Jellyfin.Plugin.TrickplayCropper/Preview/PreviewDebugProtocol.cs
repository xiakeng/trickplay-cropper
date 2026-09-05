using Jellyfin.Plugin.TrickplayCropper.Caching;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Owns the stable Debug identities and fields of the Preview decision and coordination protocol.
/// </summary>
/// <remarks>
/// Every event is Debug-only, redaction-safe, deterministic in identity and fields, and behavior-neutral.
/// The generated guards skip all field construction when the host disables Debug logging, so an
/// Information-level host pays nothing for this smoke-protocol seam. These events never carry credentials,
/// authorization data, claims, user names, media titles, media paths, Source Sprite paths, or Cache Tree paths.
/// </remarks>
internal static partial class PreviewDebugProtocol
{
    /// <summary>
    /// Records one expected resolution-unavailable outcome, staying silent for a concealed outcome.
    /// </summary>
    /// <param name="logger">The category logger of the module that resolved the outcome.</param>
    /// <param name="reason">The stable unavailable reason.</param>
    public static void LogUnavailable(ILogger logger, PreviewUnavailableReason reason)
    {
        if (reason != PreviewUnavailableReason.Concealed)
        {
            LogUnavailableReason(logger, reason);
        }
    }

    /// <summary>
    /// Records the Frame Index and sprite index selected for one resolved GET.
    /// </summary>
    /// <param name="logger">The category logger of the module that selected the frame.</param>
    /// <param name="frameIndex">The clamped zero-based Frame Index.</param>
    /// <param name="spriteIndex">The Source Sprite index that carries the frame.</param>
    [LoggerMessage(
        EventId = 1002,
        EventName = "TrickplayPreviewFrameSelected",
        Level = LogLevel.Debug,
        Message = """TrickplayDebug {{"EventId":1002,"EventName":"TrickplayPreviewFrameSelected","FrameIndex":{FrameIndex},"SpriteIndex":{SpriteIndex}}}""")]
    public static partial void LogFrameSelected(ILogger logger, int frameIndex, int spriteIndex);

    /// <summary>
    /// Records whether the Preview Cache Entry was read or generated for one served GET.
    /// </summary>
    /// <param name="logger">The category logger of the module that accessed the Preview Cache.</param>
    /// <param name="cacheDisposition">The Preview Cache disposition of the served response.</param>
    [LoggerMessage(
        EventId = 1003,
        EventName = "TrickplayPreviewCacheDisposition",
        Level = LogLevel.Debug,
        Message = """TrickplayDebug {{"EventId":1003,"EventName":"TrickplayPreviewCacheDisposition","CacheDisposition":"{CacheDisposition}"}}""")]
    public static partial void LogCacheDisposition(ILogger logger, PreviewCacheDisposition cacheDisposition);

    /// <summary>
    /// Records that one operation is waiting for exclusive Preview Cache Entry ownership.
    /// </summary>
    /// <param name="logger">The logger that reports Preview Cache Entry coordination.</param>
    [LoggerMessage(
        EventId = 1004,
        EventName = "TrickplayPreviewEntryLockWaiting",
        Level = LogLevel.Debug,
        Message = """TrickplayDebug {{"EventId":1004,"EventName":"TrickplayPreviewEntryLockWaiting"}}""")]
    public static partial void LogEntryLockWaiting(ILogger logger);

    /// <summary>
    /// Records that one operation has taken exclusive Preview Cache Entry ownership.
    /// </summary>
    /// <param name="logger">The logger that reports Preview Cache Entry coordination.</param>
    [LoggerMessage(
        EventId = 1005,
        EventName = "TrickplayPreviewEntryLockOwned",
        Level = LogLevel.Debug,
        Message = """TrickplayDebug {{"EventId":1005,"EventName":"TrickplayPreviewEntryLockOwned"}}""")]
    public static partial void LogEntryLockOwned(ILogger logger);

    /// <summary>
    /// Records that one operation is waiting for a Cache Tree lease.
    /// </summary>
    /// <param name="logger">The logger that reports Cache Tree coordination.</param>
    [LoggerMessage(
        EventId = 1006,
        EventName = "TrickplayPreviewCacheTreeLeaseWaiting",
        Level = LogLevel.Debug,
        Message = """TrickplayDebug {{"EventId":1006,"EventName":"TrickplayPreviewCacheTreeLeaseWaiting"}}""")]
    public static partial void LogCacheTreeLeaseWaiting(ILogger logger);

    /// <summary>
    /// Records that one encode is waiting for one of the process-wide decode permits.
    /// </summary>
    /// <param name="logger">The category logger of the module that gates decoding.</param>
    [LoggerMessage(
        EventId = 1007,
        EventName = "TrickplayPreviewDecodePermitWaiting",
        Level = LogLevel.Debug,
        Message = """TrickplayDebug {{"EventId":1007,"EventName":"TrickplayPreviewDecodePermitWaiting"}}""")]
    public static partial void LogDecodePermitWaiting(ILogger logger);

    [LoggerMessage(
        EventId = 1001,
        EventName = "TrickplayPreviewUnavailable",
        Level = LogLevel.Debug,
        Message = """TrickplayDebug {{"EventId":1001,"EventName":"TrickplayPreviewUnavailable","Reason":"{Reason}"}}""")]
    private static partial void LogUnavailableReason(ILogger logger, PreviewUnavailableReason reason);
}
