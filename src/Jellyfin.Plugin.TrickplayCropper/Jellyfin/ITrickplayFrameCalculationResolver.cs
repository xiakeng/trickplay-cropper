using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Loads the current Jellyfin calculation inputs and selects one Frame Index.
/// </summary>
internal interface ITrickplayFrameCalculationResolver
{
    /// <summary>
    /// Applies the shared resolution, metadata, and Frame Index rules to one effective Media Source.
    /// </summary>
    /// <param name="query">The normalized Preview query.</param>
    /// <param name="normalizationSourceWidth">The matched Media Source video-stream width.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The selected calculation or an expected unavailable result.</returns>
    Task<TrickplayFrameCalculationResolution> ResolveForPreviewAsync(
        PreviewQuery query,
        int? normalizationSourceWidth,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies the shared rules while permitting a current generated-metadata observation to be reused.
    /// </summary>
    /// <param name="query">The normalized Preview query.</param>
    /// <param name="normalizationSourceWidth">The matched Media Source video-stream width.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The selected calculation or an expected unavailable result.</returns>
    Task<TrickplayFrameCalculationResolution> ResolveForProbeAsync(
        PreviewQuery query,
        int? normalizationSourceWidth,
        CancellationToken cancellationToken);
}
