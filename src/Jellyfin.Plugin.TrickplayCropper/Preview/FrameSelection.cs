namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Identifies one frame, Source Sprite, cell, and crop rectangle.
/// </summary>
/// <param name="FrameIndex">The clamped frame index.</param>
/// <param name="SpriteIndex">The Source Sprite index.</param>
/// <param name="CropX">The crop origin on the horizontal axis.</param>
/// <param name="CropY">The crop origin on the vertical axis.</param>
/// <param name="CropWidth">The crop width.</param>
/// <param name="CropHeight">The crop height.</param>
internal sealed record FrameSelection(
    int FrameIndex,
    int SpriteIndex,
    int CropX,
    int CropY,
    int CropWidth,
    int CropHeight)
{
    /// <summary>
    /// Selects the metadata-defined frame for a Jellyfin playback position.
    /// </summary>
    /// <param name="metadata">The trusted Jellyfin trickplay metadata.</param>
    /// <param name="positionTicks">The non-negative playback position.</param>
    /// <returns>The selected Source Sprite cell and crop.</returns>
    public static FrameSelection Create(TrickplayMetadata metadata, long positionTicks)
    {
        ValidateMetadata(metadata);
        long ticksPerFrame = checked((long)metadata.IntervalMilliseconds * TimeSpan.TicksPerMillisecond);
        long rawFrameIndex = positionTicks / ticksPerFrame;
        int frameIndex = checked((int)Math.Min(rawFrameIndex, metadata.ThumbnailCount - 1L));
        long framesPerSprite = checked((long)metadata.TileWidth * metadata.TileHeight);
        int spriteIndex = checked((int)(frameIndex / framesPerSprite));
        long cellIndex = frameIndex % framesPerSprite;
        long row = cellIndex / metadata.TileWidth;
        long column = cellIndex % metadata.TileWidth;
        int cropX = checked((int)(column * metadata.FrameWidth));
        int cropY = checked((int)(row * metadata.FrameHeight));
        return new FrameSelection(
            frameIndex,
            spriteIndex,
            cropX,
            cropY,
            metadata.FrameWidth,
            metadata.FrameHeight);
    }

    private static void ValidateMetadata(TrickplayMetadata metadata)
    {
        if (metadata.FrameWidth <= 0
            || metadata.FrameHeight <= 0
            || metadata.IntervalMilliseconds <= 0
            || metadata.TileWidth <= 0
            || metadata.TileHeight <= 0
            || metadata.ThumbnailCount <= 0)
        {
            throw new InvalidDataException("Jellyfin trickplay metadata contains a non-positive required value.");
        }
    }
}
