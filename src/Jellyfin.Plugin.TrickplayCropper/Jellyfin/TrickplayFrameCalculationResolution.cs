using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed results of the shared resolution and Frame Index calculation.
/// </summary>
internal abstract record TrickplayFrameCalculationResolution
{
    /// <summary>
    /// Represents an available generated sequence and its selected Frame Index.
    /// </summary>
    /// <param name="Metadata">The exactly selected generated metadata.</param>
    /// <param name="FrameIndex">The clamped zero-based Frame Index.</param>
    internal sealed record Selected(TrickplayMetadata Metadata, int FrameIndex)
        : TrickplayFrameCalculationResolution;

    /// <summary>
    /// Represents calculation inputs that do not identify an available generated frame.
    /// </summary>
    /// <param name="Reason">The stable internal unavailability reason.</param>
    internal sealed record NotFound(PreviewUnavailableReason Reason)
        : TrickplayFrameCalculationResolution;
}
