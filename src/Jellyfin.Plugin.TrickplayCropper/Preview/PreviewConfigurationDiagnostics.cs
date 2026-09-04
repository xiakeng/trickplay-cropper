namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Carries the redaction-safe configuration and normalization values known when resolution failed.
/// </summary>
/// <remarks>
/// These values reconstruct a configuration, selection, or metadata failure without disclosing any
/// concealed, media, Source Sprite, or Cache Tree detail. Every member is optional because a failure can
/// occur before the corresponding value is derived.
/// </remarks>
internal sealed record PreviewConfigurationDiagnostics
{
    /// <summary>
    /// Gets the configured Trickplay Resolution Targets, or null when the snapshot was unreadable.
    /// </summary>
    public IReadOnlyList<int>? ConfiguredTargets { get; init; }

    /// <summary>
    /// Gets the minimum positive configured target the selection policy chose, when one was chosen.
    /// </summary>
    public int? ChosenTarget { get; init; }

    /// <summary>
    /// Gets the normalized even Selected Trickplay Resolution, when one was derived.
    /// </summary>
    public int? SelectedResolution { get; init; }

    /// <summary>
    /// Gets the matched Media Source video width that normalization clamps against, or null when the source reports none.
    /// </summary>
    public int? NormalizationSourceWidth { get; init; }

    /// <summary>
    /// Gets the ascending generated metadata resolution keys, or null when metadata was not reached.
    /// </summary>
    public IReadOnlyList<int>? GeneratedKeys { get; init; }
}
