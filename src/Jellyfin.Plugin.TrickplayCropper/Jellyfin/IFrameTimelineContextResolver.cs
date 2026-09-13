using System.Security.Claims;
namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves a user-authorized Frame Timeline without representation work or observation publication.
/// </summary>
internal interface IFrameTimelineContextResolver
{
    Task<FrameTimelineContextResolution> ResolveAsync(
        Guid itemId,
        Guid? mediaSourceId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}
