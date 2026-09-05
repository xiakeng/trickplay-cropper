using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed results of resolving generated metadata for one Selected Trickplay Resolution.
/// </summary>
internal abstract record TrickplayMetadataResolution
{
    /// <summary>
    /// Represents usable generated metadata.
    /// </summary>
    /// <param name="Metadata">The immutable generated metadata observation.</param>
    internal sealed record Available(TrickplayMetadata Metadata) : TrickplayMetadataResolution;

    /// <summary>
    /// Represents an expected generated-metadata absence.
    /// </summary>
    /// <param name="Reason">The stable internal unavailability reason.</param>
    internal sealed record NotFound(PreviewUnavailableReason Reason) : TrickplayMetadataResolution;
}
