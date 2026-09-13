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
internal sealed class JellyfinPreviewContextResolver : IFrameTimelineContextResolver, IPreviewContextResolver
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

        UserResolution userResolution = ResolveCurrentUser(principal);
        if (userResolution is not UserResolution.Resolved resolvedUser)
        {
            return MapPreviewUserFailure(userResolution);
        }

        using TrickplaySourceFactsCache.PreviewObservation sourceObservation = sourceFactsCache.BeginForPreview(query);
        AuthorizedSourceResolution sourceResolution = await ResolveSourceAsync(
            query.ItemId,
            query.ResolvedMediaSourceId,
            resolvedUser.User,
            cancellationToken).ConfigureAwait(false);
        if (sourceResolution is not AuthorizedSourceResolution.Resolved resolvedSource)
        {
            return MapPreviewSourceFailure(sourceResolution);
        }

        sourceFactsCache.PublishForPreview(sourceObservation, resolvedSource.NormalizationSourceWidth);
        TrickplayFrameCalculationResolution calculation = await calculationResolver
            .ResolveForPreviewAsync(query, resolvedSource.NormalizationSourceWidth, cancellationToken)
            .ConfigureAwait(false);
        return calculation switch
        {
            TrickplayFrameCalculationResolution.Selected selected => new PreviewContextResolution.Resolved(
                new PreviewContext(
                    query.ResolvedMediaSourceId,
                    resolvedSource.SourceVideo,
                    selected.Metadata,
                    selected.FrameIndex)),
            TrickplayFrameCalculationResolution.NotFound notFound => new PreviewContextResolution.NotFound(
                notFound.Reason),
            _ => throw new InvalidOperationException(
                $"Unknown Trickplay Frame calculation {calculation.GetType().Name}."),
        };
    }

    /// <inheritdoc />
    public async Task<FrameTimelineContextResolution> ResolveAsync(
        Guid itemId,
        Guid? mediaSourceId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        UserResolution userResolution = ResolveCurrentUser(principal);
        if (userResolution is not UserResolution.Resolved resolvedUser)
        {
            return MapTimelineUserFailure(userResolution);
        }

        Guid resolvedMediaSourceId = mediaSourceId ?? itemId;
        AuthorizedSourceResolution sourceResolution = await ResolveSourceAsync(
            itemId,
            resolvedMediaSourceId,
            resolvedUser.User,
            cancellationToken).ConfigureAwait(false);
        if (sourceResolution is not AuthorizedSourceResolution.Resolved resolvedSource)
        {
            return MapTimelineSourceFailure(sourceResolution);
        }

        FrameTimelineCalculationResolution calculation = await calculationResolver
            .ResolveForFrameTimelineAsync(
                resolvedMediaSourceId,
                resolvedSource.NormalizationSourceWidth,
                cancellationToken)
            .ConfigureAwait(false);
        return calculation switch
        {
            FrameTimelineCalculationResolution.Available available =>
                new FrameTimelineContextResolution.Resolved(
                    available.IntervalTicks,
                    available.FrameCount),
            FrameTimelineCalculationResolution.NotFound notFound =>
                new FrameTimelineContextResolution.NotFound(notFound.Reason),
            _ => throw new InvalidOperationException(
                $"Unknown Frame Timeline calculation {calculation.GetType().Name}."),
        };
    }

    private UserResolution ResolveCurrentUser(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return new UserResolution.Unauthorized();
        }

        Claim? userIdClaim = principal.Claims.FirstOrDefault(
            claim => claim.Type.Equals(JellyfinUserIdClaim, StringComparison.OrdinalIgnoreCase));
        bool hasUserId = Guid.TryParse(userIdClaim?.Value, out Guid userId) && userId != Guid.Empty;
        User? user = hasUserId ? userManager.GetUserById(userId) : null;
        return user is not null
            ? new UserResolution.Resolved(user)
            : IsApiKey(principal)
                ? new UserResolution.Forbidden()
                : new UserResolution.Unauthorized();
    }

    private static bool IsApiKey(ClaimsPrincipal principal)
    {
        Claim? apiKeyClaim = principal.Claims.FirstOrDefault(
            claim => claim.Type.Equals(JellyfinIsApiKeyClaim, StringComparison.OrdinalIgnoreCase));
        return bool.TryParse(apiKeyClaim?.Value, out bool isApiKey) && isApiKey;
    }

    private async Task<AuthorizedSourceResolution> ResolveSourceAsync(
        Guid itemId,
        Guid mediaSourceId,
        User user,
        CancellationToken cancellationToken)
    {
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

        return new AuthorizedSourceResolution.Resolved(sourceVideo, matchedSource.VideoStream?.Width);
    }

    private static PreviewContextResolution MapPreviewUserFailure(UserResolution resolution)
    {
        return resolution switch
        {
            UserResolution.Unauthorized => new PreviewContextResolution.Unauthorized(),
            UserResolution.Forbidden => new PreviewContextResolution.Forbidden(),
            _ => throw new InvalidOperationException(
                $"Unknown current-user resolution {resolution.GetType().Name}."),
        };
    }

    private static PreviewContextResolution MapPreviewSourceFailure(AuthorizedSourceResolution resolution)
    {
        return resolution switch
        {
            AuthorizedSourceResolution.Forbidden => new PreviewContextResolution.Forbidden(),
            AuthorizedSourceResolution.NotFound =>
                new PreviewContextResolution.NotFound(PreviewUnavailableReason.Concealed),
            _ => throw new InvalidOperationException(
                $"Unknown authorized-source resolution {resolution.GetType().Name}."),
        };
    }

    private static FrameTimelineContextResolution MapTimelineUserFailure(UserResolution resolution)
    {
        return resolution switch
        {
            UserResolution.Unauthorized => new FrameTimelineContextResolution.Unauthorized(),
            UserResolution.Forbidden => new FrameTimelineContextResolution.Forbidden(),
            _ => throw new InvalidOperationException(
                $"Unknown current-user resolution {resolution.GetType().Name}."),
        };
    }

    private static FrameTimelineContextResolution MapTimelineSourceFailure(AuthorizedSourceResolution resolution)
    {
        return resolution switch
        {
            AuthorizedSourceResolution.Forbidden => new FrameTimelineContextResolution.Forbidden(),
            AuthorizedSourceResolution.NotFound =>
                new FrameTimelineContextResolution.NotFound(PreviewUnavailableReason.Concealed),
            _ => throw new InvalidOperationException(
                $"Unknown authorized-source resolution {resolution.GetType().Name}."),
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

        internal sealed record Forbidden : AuthorizedSourceResolution;

        internal sealed record NotFound : AuthorizedSourceResolution;
    }

    private abstract record UserResolution
    {
        internal sealed record Resolved(User User) : UserResolution;

        internal sealed record Unauthorized : UserResolution;

        internal sealed record Forbidden : UserResolution;
    }
}
