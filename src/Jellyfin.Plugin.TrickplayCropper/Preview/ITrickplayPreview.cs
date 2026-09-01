using System.Security.Claims;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Coordinates one authenticated Trickplay Preview request.
/// </summary>
public interface ITrickplayPreview
{
    /// <summary>
    /// Resolves, caches, and returns the requested Trickplay Preview outcome.
    /// </summary>
    /// <param name="query">The normalized preview query.</param>
    /// <param name="user">The current request principal.</param>
    /// <param name="conditionalEntityTags">The parsed conditional entity tags.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The closed request outcome.</returns>
    Task<PreviewOutcome> GetAsync(
        PreviewQuery query,
        ClaimsPrincipal user,
        IReadOnlyCollection<EntityTagHeaderValue> conditionalEntityTags,
        CancellationToken cancellationToken);
}
