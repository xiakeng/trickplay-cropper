using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves the Preview context that GET and the Trickplay Frame Probe share.
/// </summary>
internal interface IPreviewContextResolver
{
    /// <summary>
    /// Validates the request, authorizes the logical video, proves Media Source membership,
    /// selects the exact generated metadata, and calculates the Frame Index.
    /// </summary>
    /// <param name="query">The normalized preview query.</param>
    /// <param name="principal">The current request principal.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The typed shared-context result.</returns>
    Task<PreviewContextResolution> ResolveAsync(
        PreviewQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}
