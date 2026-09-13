using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Library;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves the user-authorized Preview context through Jellyfin-owned managers.
/// </summary>
internal sealed class JellyfinPreviewContextResolver : IPreviewContextResolver
{
    /// <summary>The native API-key identity claim type.</summary>
    internal const string JellyfinIsApiKeyClaim = "Jellyfin-IsApiKey";

    /// <summary>The native user identity claim type.</summary>
    internal const string JellyfinUserIdClaim = "Jellyfin-UserId";

    private readonly IUserManager userManager;
    private readonly ILibraryManager libraryManager;
    private readonly IMediaSourceManager mediaSourceManager;
    private readonly ITrickplayFrameCalculationResolver calculationResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPreviewContextResolver"/> class.
    /// </summary>
    /// <param name="userManager">Resolves the current Jellyfin user.</param>
    /// <param name="libraryManager">Resolves user-visible logical and Source Videos.</param>
    /// <param name="mediaSourceManager">Enumerates the authorized logical video's Media Sources.</param>
    /// <param name="calculationResolver">Performs the shared resolution and Frame Index calculation.</param>
    public JellyfinPreviewContextResolver(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        ITrickplayFrameCalculationResolver calculationResolver)
    {
        this.userManager = userManager;
        this.libraryManager = libraryManager;
        this.mediaSourceManager = mediaSourceManager;
        this.calculationResolver = calculationResolver;
    }

    /// <inheritdoc />
    public async Task<PreviewContextResolution> ResolveAsync(
        PreviewQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        AuthorizedSourceResolution authorization = await ResolveAuthorizedSourceAsync(
            new PreviewSourceQuery(query.ItemId, query.MediaSourceId),
            principal,
            cancellationToken).ConfigureAwait(false);
        if (authorization is not AuthorizedSourceResolution.Resolved resolved)
        {
            return MapAuthorization(authorization);
        }

        TrickplayFrameCalculationResolution calculation = await calculationResolver
            .ResolveForPreviewAsync(query, resolved.NormalizationSourceWidth, cancellationToken)
            .ConfigureAwait(false);
        return calculation switch
        {
            TrickplayFrameCalculationResolution.Selected selected => new PreviewContextResolution.Resolved(
                new PreviewContext(query.ResolvedMediaSourceId, resolved.SourceVideo, selected.Metadata, selected.FrameIndex)),
            TrickplayFrameCalculationResolution.BadRequest => new PreviewContextResolution.BadRequest(),
            TrickplayFrameCalculationResolution.NotFound notFound => new PreviewContextResolution.NotFound(
                notFound.Reason),
            _ => throw new InvalidOperationException(
                $"Unknown Trickplay Frame calculation {calculation.GetType().Name}."),
        };
    }

    /// <summary>
    /// Resolves the current-user authorization and selected source without calculation or image work.
    /// </summary>
    /// <param name="query">The logical Item and selected Media Source.</param>
    /// <param name="principal">The current request principal.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The authorized source or a closed failure.</returns>
    internal async Task<AuthorizedSourceResolution> ResolveAuthorizedSourceAsync(
        PreviewSourceQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return new AuthorizedSourceResolution.Unauthorized();
        }

        User? user = ResolveUser(principal);
        if (user is null)
        {
            return IsApiKey(principal)
                ? new AuthorizedSourceResolution.Forbidden()
                : new AuthorizedSourceResolution.Unauthorized();
        }

        Video? logicalVideo = libraryManager.GetItemById<Video>(query.ItemId, user);
        if (logicalVideo?.Id != query.ItemId)
        {
            return new AuthorizedSourceResolution.NotFound();
        }

        if (logicalVideo.GetPlayAccess(user) != PlayAccess.Full)
        {
            return new AuthorizedSourceResolution.Forbidden();
        }

        IReadOnlyList<MediaSourceInfo> mediaSources = await mediaSourceManager.GetPlaybackMediaSources(
            logicalVideo,
            user,
            allowMediaProbe: true,
            enablePathSubstitution: false,
            cancellationToken).ConfigureAwait(false);
        MediaSourceInfo? matchedSource = mediaSources.FirstOrDefault(
            source => IsSelectedSource(source, query.ResolvedMediaSourceId));
        if (matchedSource is null)
        {
            return new AuthorizedSourceResolution.NotFound();
        }

        Video? sourceVideo = libraryManager.GetItemById<Video>(query.ResolvedMediaSourceId, user);
        if (sourceVideo?.Id != query.ResolvedMediaSourceId)
        {
            return new AuthorizedSourceResolution.NotFound();
        }

        int? normalizationSourceWidth = matchedSource.VideoStream?.Width;
        return new AuthorizedSourceResolution.Resolved(sourceVideo, normalizationSourceWidth);
    }

    private User? ResolveUser(ClaimsPrincipal principal)
    {
        Claim? userIdClaim = principal.Claims.FirstOrDefault(
            claim => claim.Type.Equals(JellyfinUserIdClaim, StringComparison.OrdinalIgnoreCase));
        bool hasUserId = Guid.TryParse(userIdClaim?.Value, out Guid userId) && userId != Guid.Empty;
        return hasUserId
            ? userManager.GetUserById(userId)
            : null;
    }

    private static bool IsApiKey(ClaimsPrincipal principal)
    {
        Claim? apiKeyClaim = principal.Claims.FirstOrDefault(
            claim => claim.Type.Equals(JellyfinIsApiKeyClaim, StringComparison.OrdinalIgnoreCase));
        return bool.TryParse(apiKeyClaim?.Value, out bool isApiKey) && isApiKey;
    }

    private static PreviewContextResolution MapAuthorization(AuthorizedSourceResolution resolution)
    {
        return resolution switch
        {
            AuthorizedSourceResolution.BadRequest => new PreviewContextResolution.BadRequest(),
            AuthorizedSourceResolution.Unauthorized => new PreviewContextResolution.Unauthorized(),
            AuthorizedSourceResolution.Forbidden => new PreviewContextResolution.Forbidden(),
            AuthorizedSourceResolution.NotFound => new PreviewContextResolution.NotFound(
                PreviewUnavailableReason.Concealed),
            _ => throw new InvalidOperationException(
                $"Unknown authorized source resolution {resolution.GetType().Name}."),
        };
    }

    private static bool IsSelectedSource(MediaSourceInfo source, Guid mediaSourceId)
    {
        return Guid.TryParse(source.Id, out Guid candidateId) && candidateId == mediaSourceId;
    }
}
