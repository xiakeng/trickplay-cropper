namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed results of resolving user-independent source facts.
/// </summary>
internal abstract record TrickplaySourceFactsResolution
{
    /// <summary>
    /// Represents verified Item membership and the matched Media Source video width.
    /// </summary>
    /// <param name="NormalizationSourceWidth">The matched Media Source video-stream width.</param>
    internal sealed record Available(int? NormalizationSourceWidth) : TrickplaySourceFactsResolution;

    /// <summary>
    /// Represents explicit user-independent source absence.
    /// </summary>
    internal sealed record NotFound : TrickplaySourceFactsResolution;
}
