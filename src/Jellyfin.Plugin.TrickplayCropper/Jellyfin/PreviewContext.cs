using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Carries everything the user-authorized GET Preview context decided for one request.
/// </summary>
/// <param name="MediaSourceId">The effective media source identifier.</param>
/// <param name="SourceVideo">The user-visible Source Video selected by the authorized logical video.</param>
/// <param name="Metadata">The exactly selected generated metadata.</param>
/// <param name="FrameIndex">The clamped zero-based Frame Index.</param>
internal sealed record PreviewContext(
    Guid MediaSourceId,
    Video SourceVideo,
    TrickplayMetadata Metadata,
    int FrameIndex);
