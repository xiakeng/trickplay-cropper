namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Answers the Trickplay Frame Probe for one authenticated Preview request.
/// </summary>
public interface ITrickplayFrameProbe
{
    /// <summary>
    /// Selects the Frame Index a playback position identifies through user-independent source facts and
    /// shared calculation rules, without resolving a Source Sprite or performing representation work.
    /// </summary>
    /// <param name="query">The normalized preview query.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The closed probe outcome.</returns>
    Task<TrickplayFrameProbeOutcome> ProbeAsync(
        PreviewQuery query,
        CancellationToken cancellationToken);
}
