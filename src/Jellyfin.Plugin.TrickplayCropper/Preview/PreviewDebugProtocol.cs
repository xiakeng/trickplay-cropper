using Jellyfin.Plugin.TrickplayCropper.Caching;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Owns the stable Debug identities and fields of the Preview decision protocol.
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
        Message = "Trickplay Preview selected FrameIndex {FrameIndex} on SpriteIndex {SpriteIndex}.")]
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
        Message = "Trickplay Preview served from cache with disposition {CacheDisposition}.")]
    public static partial void LogCacheDisposition(ILogger logger, PreviewCacheDisposition cacheDisposition);

    [LoggerMessage(
        EventId = 1001,
        EventName = "TrickplayPreviewUnavailable",
        Level = LogLevel.Debug,
        Message = "Trickplay Preview resolved unavailable with reason {Reason}.")]
    private static partial void LogUnavailableReason(ILogger logger, PreviewUnavailableReason reason);
}
