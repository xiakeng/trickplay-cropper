namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Carries only redaction-safe diagnostic values that were known when Preview processing failed.
/// </summary>
internal sealed record PreviewFailureDetails
{
    /// <summary>
    /// Gets the actual decoded source height when a codec was opened.
    /// </summary>
    public int? ActualHeight { get; init; }

    /// <summary>
    /// Gets the actual decoded source width when a codec was opened.
    /// </summary>
    public int? ActualWidth { get; init; }

    /// <summary>
    /// Gets the stable image-processing path name.
    /// </summary>
    public string? DecodePath { get; init; }

    /// <summary>
    /// Gets the stable name of the failed validation.
    /// </summary>
    public string? FailedValidation { get; init; }

    /// <summary>
    /// Gets the numeric value that failed validation.
    /// </summary>
    public long? FailedValue { get; init; }

    /// <summary>
    /// Gets the metadata snapshot available at failure time.
    /// </summary>
    public TrickplayMetadata? Metadata { get; init; }

    /// <summary>
    /// Gets the selected frame and crop available at failure time.
    /// </summary>
    public FrameSelection? Selection { get; init; }

    /// <summary>
    /// Gets the redaction-safe Skia result name when one was returned.
    /// </summary>
    public string? SkiaResult { get; init; }

    /// <summary>
    /// Gets the captured Source Sprite UTC modification ticks when available.
    /// </summary>
    public long? SourceLastWriteUtcTicks { get; init; }

    /// <summary>
    /// Gets the captured Source Sprite length when available.
    /// </summary>
    public long? SourceLength { get; init; }
}
