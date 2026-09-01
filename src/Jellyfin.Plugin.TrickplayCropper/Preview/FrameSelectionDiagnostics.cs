namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Captures the redaction-safe selection values computed before Skia coordinate narrowing completes.
/// </summary>
internal sealed record FrameSelectionDiagnostics
{
    /// <summary>
    /// Gets the zero-based column within the Source Sprite.
    /// </summary>
    public required long Column { get; init; }

    /// <summary>
    /// Gets the crop height.
    /// </summary>
    public required int CropHeight { get; init; }

    /// <summary>
    /// Gets the crop width.
    /// </summary>
    public required int CropWidth { get; init; }

    /// <summary>
    /// Gets the crop origin on the horizontal axis before narrowing.
    /// </summary>
    public required long CropX { get; init; }

    /// <summary>
    /// Gets the crop origin on the vertical axis before narrowing.
    /// </summary>
    public required long CropY { get; init; }

    /// <summary>
    /// Gets the clamped frame index.
    /// </summary>
    public required long FrameIndex { get; init; }

    /// <summary>
    /// Gets the zero-based row within the Source Sprite.
    /// </summary>
    public required long Row { get; init; }

    /// <summary>
    /// Gets the Source Sprite index.
    /// </summary>
    public required long SpriteIndex { get; init; }
}
