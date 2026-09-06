namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum MetadataAvailability
{
    Available,
    ChangedInterval,
    ChangedWidth,
    ContradictoryFrameWidth,
    CropBottomOverflow,
    CropRightOverflow,
    CropXOverflow,
    CropYOverflow,
    ExactWidthMissing,
    FrameHeightZero,
    FrameWidthZero,
    GeneratedMetadataMissing,
    IntervalZero,
    MultipleWidths,
    NegativeThumbnails,
    NoThumbnails,
    TileHeightZero,
    TileWidthZero,
}
