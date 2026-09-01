namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Represents the exact 320px Jellyfin trickplay metadata used for selection and identity.
/// </summary>
/// <param name="FrameWidth">The width of one preview frame.</param>
/// <param name="FrameHeight">The height of one preview frame.</param>
/// <param name="IntervalMilliseconds">The interval between frames in milliseconds.</param>
/// <param name="TileWidth">The number of Source Sprite columns.</param>
/// <param name="TileHeight">The number of Source Sprite rows.</param>
/// <param name="ThumbnailCount">The authoritative number of frames.</param>
internal sealed record TrickplayMetadata(
    int FrameWidth,
    int FrameHeight,
    int IntervalMilliseconds,
    int TileWidth,
    int TileHeight,
    int ThumbnailCount);
