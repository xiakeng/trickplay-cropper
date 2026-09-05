using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves user-independent source facts for one authenticated Trickplay Frame Probe.
/// </summary>
internal interface ITrickplayFrameProbeContextResolver
{
    /// <summary>
    /// Verifies Item and Media Source identity, then performs the shared Frame Index calculation.
    /// </summary>
    /// <param name="query">The normalized Preview query.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The selected calculation or an expected unavailable result.</returns>
    Task<TrickplayFrameCalculationResolution> ResolveAsync(
        PreviewQuery query,
        CancellationToken cancellationToken);
}
