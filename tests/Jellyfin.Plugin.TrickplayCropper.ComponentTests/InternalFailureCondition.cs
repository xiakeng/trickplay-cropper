namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum InternalFailureCondition
{
    ContradictoryFrameWidth,
    FrameWidthZero,
    FrameHeightZero,
    IntervalZero,
    NoThumbnails,
    NegativeThumbnails,
    TileWidthZero,
    TileHeightZero,
    CropXOverflow,
    CropYOverflow,
    CropRightOverflow,
    CropBottomOverflow,
}
