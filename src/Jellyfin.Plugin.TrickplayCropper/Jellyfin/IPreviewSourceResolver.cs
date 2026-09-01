using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves Jellyfin managers and a manager-owned Source Sprite into a typed preview source.
/// </summary>
internal interface IPreviewSourceResolver
{
    /// <summary>
    /// Resolves the authorized source, exact 320px metadata, and Source Sprite snapshot.
    /// </summary>
    /// <param name="query">The normalized preview query.</param>
    /// <param name="principal">The current request principal.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The typed source-resolution result.</returns>
    Task<PreviewSourceResolution> ResolveAsync(
        PreviewQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}
