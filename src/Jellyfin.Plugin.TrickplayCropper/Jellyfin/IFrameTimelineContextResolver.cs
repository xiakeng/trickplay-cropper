using System.Security.Claims;
namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves a user-authorized Frame Timeline without representation work or observation publication.
/// </summary>
internal interface IFrameTimelineContextResolver
{
    /// <summary>
    /// Authorizes the logical video and selected source, then resolves its current Frame Timeline.
    /// </summary>
    /// <param name="itemId">The logical video identifier.</param>
    /// <param name="mediaSourceId">The optional alternate media source identifier.</param>
    /// <param name="principal">The current request principal.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The typed Frame Timeline context result.</returns>
    Task<FrameTimelineContextResolution> ResolveAsync(
        Guid itemId,
        Guid? mediaSourceId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}
