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
    private readonly TrickplaySourceFactsCache sourceFactsCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPreviewContextResolver"/> class.
    /// </summary>
    /// <param name="userManager">Resolves the current Jellyfin user.</param>
    /// <param name="libraryManager">Resolves user-visible logical and Source Videos.</param>
    /// <param name="mediaSourceManager">Enumerates the authorized logical video's Media Sources.</param>
    /// <param name="calculationResolver">Performs the shared resolution and Frame Index calculation.</param>
    /// <param name="sourceFactsCache">Publishes independently verified user-neutral source facts.</param>
    public JellyfinPreviewContextResolver(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        ITrickplayFrameCalculationResolver calculationResolver,
        TrickplaySourceFactsCache sourceFactsCache)
    {
        this.userManager = userManager;
        this.libraryManager = libraryManager;
        this.mediaSourceManager = mediaSourceManager;
        this.calculationResolver = calculationResolver;
        this.sourceFactsCache = sourceFactsCache;
    }

    /// <inheritdoc />
    public async Task<PreviewContextResolution> ResolveAsync(
        PreviewQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (query.PositionTicks < 0)
        {
            return new PreviewContextResolution.BadRequest();
        }

        if (principal.Identity?.IsAuthenticated != true)
        {
            return new PreviewContextResolution.Unauthorized();
        }

        User? currentUser = ResolveUser(principal);
        if (currentUser is null)
        {
            return IsApiKey(principal)
                ? new PreviewContextResolution.Forbidden()
                : new PreviewContextResolution.Unauthorized();
        }

        using TrickplaySourceFactsCache.PreviewObservation sourceObservation = sourceFactsCache.BeginForPreview(query);
        AuthorizedSourceResolution authorization = await ResolveAuthorizedSourceAsync(
            query.ItemId,
            query.ResolvedMediaSourceId,
            principal,
            currentUser,
            sourceObservation,
            cancellationToken).ConfigureAwait(false);
        if (authorization is not AuthorizedSourceResolution.Resolved resolved)
        {
            return MapPreviewAuthorization(authorization);
        }

        TrickplayFrameCalculationResolution calculation = await calculationResolver
            .ResolveForPreviewAsync(query, resolved.NormalizationSourceWidth, cancellationToken)
            .ConfigureAwait(false);
        return calculation switch
        {
            TrickplayFrameCalculationResolution.Selected selected => new PreviewContextResolution.Resolved(
                new PreviewContext(query.ResolvedMediaSourceId, resolved.SourceVideo, selected.Metadata, selected.FrameIndex)),
            TrickplayFrameCalculationResolution.NotFound notFound => new PreviewContextResolution.NotFound(
                notFound.Reason),
            _ => throw new InvalidOperationException(
                $"Unknown Trickplay Frame calculation {calculation.GetType().Name}."),
        };
    }

    /// <summary>
    /// Resolves the same current-user authorization boundary for a Frame Timeline.
    /// </summary>
    public async Task<FrameTimelineContextResolution> ResolveTimelineAsync(
        FrameTimelineQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        AuthorizedSourceResolution authorization = await ResolveAuthorizedSourceAsync(
            query.ItemId,
            query.ResolvedMediaSourceId,
            principal,
            user: null,
            sourceObservation: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (authorization is not AuthorizedSourceResolution.Resolved resolved)
        {
            return MapTimelineAuthorization(authorization);
        }

        TrickplayFrameCalculationResolution calculation = await calculationResolver
            .ResolveForTimelineAsync(query, resolved.NormalizationSourceWidth, cancellationToken)
            .ConfigureAwait(false);
        return calculation switch
        {
            TrickplayFrameCalculationResolution.Selected selected =>
                new FrameTimelineContextResolution.Resolved(selected.Metadata),
            TrickplayFrameCalculationResolution.NotFound => new FrameTimelineContextResolution.NotFound(),
            _ => throw new InvalidOperationException(
                $"Unknown Trickplay Frame calculation {calculation.GetType().Name}."),
        };
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

    private async Task<AuthorizedSourceResolution> ResolveAuthorizedSourceAsync(
        Guid itemId,
        Guid mediaSourceId,
        ClaimsPrincipal principal,
        User? user,
        TrickplaySourceFactsCache.PreviewObservation? sourceObservation,
        CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return new AuthorizedSourceResolution.Unauthorized();
        }

        user ??= ResolveUser(principal);
        if (user is null)
        {
            return IsApiKey(principal)
                ? new AuthorizedSourceResolution.Forbidden()
                : new AuthorizedSourceResolution.Unauthorized();
        }

        Video? logicalVideo = libraryManager.GetItemById<Video>(itemId, user);
        if (logicalVideo?.Id != itemId)
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
            source => IsSelectedSource(source, mediaSourceId));
        if (matchedSource is null)
        {
            return new AuthorizedSourceResolution.NotFound();
        }

        Video? sourceVideo = libraryManager.GetItemById<Video>(mediaSourceId, user);
        if (sourceVideo?.Id != mediaSourceId)
        {
            return new AuthorizedSourceResolution.NotFound();
        }

        int? normalizationSourceWidth = matchedSource.VideoStream?.Width;
        if (sourceObservation is not null)
        {
            sourceFactsCache.PublishForPreview(sourceObservation, normalizationSourceWidth);
        }

        return new AuthorizedSourceResolution.Resolved(sourceVideo, normalizationSourceWidth);
    }

    private static PreviewContextResolution MapPreviewAuthorization(AuthorizedSourceResolution authorization)
    {
        return authorization switch
        {
            AuthorizedSourceResolution.BadRequest => new PreviewContextResolution.BadRequest(),
            AuthorizedSourceResolution.Unauthorized => new PreviewContextResolution.Unauthorized(),
            AuthorizedSourceResolution.Forbidden => new PreviewContextResolution.Forbidden(),
            AuthorizedSourceResolution.NotFound => new PreviewContextResolution.NotFound(
                PreviewUnavailableReason.Concealed),
            _ => throw new InvalidOperationException(
                $"Unknown authorization resolution {authorization.GetType().Name}."),
        };
    }

    private static FrameTimelineContextResolution MapTimelineAuthorization(
        AuthorizedSourceResolution authorization)
    {
        return authorization switch
        {
            AuthorizedSourceResolution.BadRequest => new FrameTimelineContextResolution.BadRequest(),
            AuthorizedSourceResolution.Unauthorized => new FrameTimelineContextResolution.Unauthorized(),
            AuthorizedSourceResolution.Forbidden => new FrameTimelineContextResolution.Forbidden(),
            AuthorizedSourceResolution.NotFound => new FrameTimelineContextResolution.NotFound(),
            _ => throw new InvalidOperationException(
                $"Unknown authorization resolution {authorization.GetType().Name}."),
        };
    }

    private static bool IsSelectedSource(MediaSourceInfo source, Guid mediaSourceId)
    {
        return Guid.TryParse(source.Id, out Guid candidateId) && candidateId == mediaSourceId;
    }

    private abstract record AuthorizedSourceResolution
    {
        internal sealed record Resolved(Video SourceVideo, int? NormalizationSourceWidth)
            : AuthorizedSourceResolution;

        internal sealed record BadRequest : AuthorizedSourceResolution;

        internal sealed record Unauthorized : AuthorizedSourceResolution;

        internal sealed record Forbidden : AuthorizedSourceResolution;

        internal sealed record NotFound : AuthorizedSourceResolution;
    }
}
