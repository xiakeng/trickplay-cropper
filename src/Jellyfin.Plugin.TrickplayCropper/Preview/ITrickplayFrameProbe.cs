using System.Security.Claims;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Answers the Trickplay Frame Probe for one authenticated Preview request.
/// </summary>
public interface ITrickplayFrameProbe
{
    /// <summary>
    /// Selects the Frame Index a playback position identifies through the shared Preview context,
    /// without resolving a Source Sprite or performing any other representation work.
    /// </summary>
    /// <param name="query">The normalized preview query.</param>
    /// <param name="user">The current request principal.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The closed probe outcome.</returns>
    Task<TrickplayFrameProbeOutcome> ProbeAsync(
        PreviewQuery query,
        ClaimsPrincipal user,
        CancellationToken cancellationToken);
}
