namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Represents the exact generated Jellyfin trickplay metadata selected for one Preview context.
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
    int ThumbnailCount)
{
    /// <summary>
    /// Rejects metadata that cannot describe one generated frame sequence.
    /// </summary>
    public void Validate()
    {
        ValidatePositive(FrameWidth, "FrameWidthPositive");
        ValidatePositive(FrameHeight, "FrameHeightPositive");
        ValidatePositive(IntervalMilliseconds, "IntervalMillisecondsPositive");
        ValidatePositive(TileWidth, "TileWidthPositive");
        ValidatePositive(TileHeight, "TileHeightPositive");
        ValidatePositive(ThumbnailCount, "ThumbnailCountPositive");
    }

    /// <summary>
    /// Rejects invalid representation geometry while allowing a non-positive playback interval.
    /// </summary>
    public void ValidateForPreview()
    {
        ValidatePositive(FrameWidth, "FrameWidthPositive");
        ValidatePositive(FrameHeight, "FrameHeightPositive");
        ValidatePositive(TileWidth, "TileWidthPositive");
        ValidatePositive(TileHeight, "TileHeightPositive");
        ValidatePositive(ThumbnailCount, "ThumbnailCountPositive");
    }

    private void ValidatePositive(int value, string validation)
    {
        if (value <= 0)
        {
            throw new InvalidTrickplayMetadataException(this, validation, value);
        }
    }
}
