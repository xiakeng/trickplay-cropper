namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Identifies one frame, Source Sprite, cell, and crop rectangle.
/// </summary>
/// <param name="FrameIndex">The clamped frame index.</param>
/// <param name="SpriteIndex">The Source Sprite index.</param>
/// <param name="Row">The zero-based row within the Source Sprite.</param>
/// <param name="Column">The zero-based column within the Source Sprite.</param>
/// <param name="CropX">The crop origin on the horizontal axis.</param>
/// <param name="CropY">The crop origin on the vertical axis.</param>
/// <param name="CropWidth">The crop width.</param>
/// <param name="CropHeight">The crop height.</param>
internal sealed record FrameSelection(
    int FrameIndex,
    int SpriteIndex,
    int Row,
    int Column,
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
        if (positionTicks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(positionTicks),
                positionTicks,
                "The playback position must be non-negative.");
        }

        ValidateMetadata(metadata);
        long ticksPerFrame = checked((long)metadata.IntervalMilliseconds * TimeSpan.TicksPerMillisecond);
        long rawFrameIndex = positionTicks / ticksPerFrame;
        long selectedFrameIndex = Math.Min(rawFrameIndex, metadata.ThumbnailCount - 1L);
        long framesPerSprite = checked((long)metadata.TileWidth * metadata.TileHeight);
        long selectedSpriteIndex = selectedFrameIndex / framesPerSprite;
        long cellIndex = selectedFrameIndex % framesPerSprite;
        long row = cellIndex / metadata.TileWidth;
        long column = cellIndex % metadata.TileWidth;
        long cropX = checked(column * metadata.FrameWidth);
        long cropY = checked(row * metadata.FrameHeight);
        return new FrameSelection(
            ConvertToInt32(selectedFrameIndex, metadata, "FrameIndexInt32"),
            ConvertToInt32(selectedSpriteIndex, metadata, "SpriteIndexInt32"),
            ConvertToInt32(row, metadata, "RowInt32"),
            ConvertToInt32(column, metadata, "ColumnInt32"),
            ConvertToInt32(cropX, metadata, "CropXInt32"),
            ConvertToInt32(cropY, metadata, "CropYInt32"),
            metadata.FrameWidth,
            metadata.FrameHeight);
    }

    private static void ValidateMetadata(TrickplayMetadata metadata)
    {
        ValidatePositive(metadata.FrameWidth, metadata, "FrameWidthPositive");
        ValidatePositive(metadata.FrameHeight, metadata, "FrameHeightPositive");
        ValidatePositive(metadata.IntervalMilliseconds, metadata, "IntervalMillisecondsPositive");
        ValidatePositive(metadata.TileWidth, metadata, "TileWidthPositive");
        ValidatePositive(metadata.TileHeight, metadata, "TileHeightPositive");
        ValidatePositive(metadata.ThumbnailCount, metadata, "ThumbnailCountPositive");
    }

    private static void ValidatePositive(long value, TrickplayMetadata metadata, string validation)
    {
        if (value <= 0)
        {
            throw new InvalidTrickplayMetadataException(metadata, validation, value);
        }
    }

    private static int ConvertToInt32(long value, TrickplayMetadata metadata, string validation)
    {
        try
        {
            return checked((int)value);
        }
        catch (OverflowException)
        {
            throw new InvalidTrickplayMetadataException(metadata, validation, value);
        }
    }
}
