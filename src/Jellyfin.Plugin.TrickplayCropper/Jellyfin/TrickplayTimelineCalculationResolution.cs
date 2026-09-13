using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed results of resolving one Frame Timeline's calculation inputs.
/// </summary>
internal abstract record TrickplayTimelineCalculationResolution
{
    /// <summary>Represents a usable generated frame sequence.</summary>
    /// <param name="Metadata">The exact generated metadata row.</param>
    internal sealed record Selected(TrickplayMetadata Metadata)
        : TrickplayTimelineCalculationResolution;

    /// <summary>Represents unavailable generated data.</summary>
    /// <param name="Reason">The stable internal unavailability reason.</param>
    internal sealed record NotFound(PreviewUnavailableReason Reason)
        : TrickplayTimelineCalculationResolution;
}
