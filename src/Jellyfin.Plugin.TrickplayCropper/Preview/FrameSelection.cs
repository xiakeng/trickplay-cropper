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
    /// Locates the Source Sprite cell and crop rectangle of an already selected frame.
    /// </summary>
    /// <param name="metadata">The trusted Jellyfin trickplay metadata.</param>
    /// <param name="frameIndex">The clamped Frame Index from the shared Preview context.</param>
    /// <returns>The selected Source Sprite cell and crop.</returns>
    public static FrameSelection Create(TrickplayMetadata metadata, int frameIndex)
    {
        long selectedFrameIndex = frameIndex;
        long framesPerSprite = checked((long)metadata.TileWidth * metadata.TileHeight);
        long selectedSpriteIndex = selectedFrameIndex / framesPerSprite;
        long cellIndex = selectedFrameIndex % framesPerSprite;
        long row = cellIndex / metadata.TileWidth;
        long column = cellIndex % metadata.TileWidth;
        long cropX = checked(column * metadata.FrameWidth);
        long cropY = checked(row * metadata.FrameHeight);
        long cropRight = checked(cropX + metadata.FrameWidth);
        long cropBottom = checked(cropY + metadata.FrameHeight);
        var diagnostics = new FrameSelectionDiagnostics
        {
            FrameIndex = selectedFrameIndex,
            SpriteIndex = selectedSpriteIndex,
            Row = row,
            Column = column,
            CropX = cropX,
            CropY = cropY,
            CropWidth = metadata.FrameWidth,
            CropHeight = metadata.FrameHeight,
        };
        int normalizedCropX = ConvertToInt32(cropX, metadata, diagnostics, "CropXInt32");
        int normalizedCropY = ConvertToInt32(cropY, metadata, diagnostics, "CropYInt32");
        _ = ConvertToInt32(cropRight, metadata, diagnostics, "CropRightInt32");
        _ = ConvertToInt32(cropBottom, metadata, diagnostics, "CropBottomInt32");
        return new FrameSelection(
            ConvertToInt32(selectedFrameIndex, metadata, diagnostics, "FrameIndexInt32"),
            ConvertToInt32(selectedSpriteIndex, metadata, diagnostics, "SpriteIndexInt32"),
            ConvertToInt32(row, metadata, diagnostics, "RowInt32"),
            ConvertToInt32(column, metadata, diagnostics, "ColumnInt32"),
            normalizedCropX,
            normalizedCropY,
            metadata.FrameWidth,
            metadata.FrameHeight);
    }

    private static int ConvertToInt32(
        long value,
        TrickplayMetadata metadata,
        FrameSelectionDiagnostics diagnostics,
        string validation)
    {
        try
        {
            return checked((int)value);
        }
        catch (OverflowException)
        {
            throw new InvalidTrickplayMetadataException(metadata, validation, value)
            {
                SelectionDiagnostics = diagnostics,
            };
        }
    }
}
