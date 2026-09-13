using System.Security.Claims;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Coordinates one authenticated Frame Timeline request.
/// </summary>
public interface ITrickplayFrameTimeline
{
    /// <summary>
    /// Returns the current authorized Frame Timeline outcome.
    /// </summary>
    Task<TrickplayFrameTimelineOutcome> GetAsync(
        Guid itemId,
        Guid? mediaSourceId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken);
}
