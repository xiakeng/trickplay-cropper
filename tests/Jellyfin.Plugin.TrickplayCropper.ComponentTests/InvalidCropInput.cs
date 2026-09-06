namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum InvalidCropInput
{
    NegativeX,
    NegativeY,
    NegativeWidth,
    NegativeHeight,
    ZeroWidth,
    ZeroHeight,
}
