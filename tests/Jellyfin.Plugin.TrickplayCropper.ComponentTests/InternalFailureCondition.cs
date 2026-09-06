namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum InternalFailureCondition
{
    ContradictoryFrameWidth,
    FrameWidthZero,
    FrameHeightZero,
    IntervalZero,
    TileWidthZero,
    TileHeightZero,
    CropXOverflow,
    CropYOverflow,
    CropRightOverflow,
    CropBottomOverflow,
}
