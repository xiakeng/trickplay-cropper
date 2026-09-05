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
    private const string JellyfinIsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string JellyfinUserIdClaim = "Jellyfin-UserId";

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
        if (query.PositionTicks < 0)
        {
            return new PreviewContextResolution.BadRequest();
        }

        if (principal.Identity?.IsAuthenticated != true)
        {
            return new PreviewContextResolution.Unauthorized();
        }

        User? user = ResolveUser(principal);
        if (user is null)
        {
            return IsApiKey(principal)
                ? new PreviewContextResolution.Forbidden()
                : new PreviewContextResolution.Unauthorized();
        }

        Video? logicalVideo = libraryManager.GetItemById<Video>(query.ItemId, user);
        if (logicalVideo?.Id != query.ItemId)
        {
            return Concealed();
        }

        if (logicalVideo.GetPlayAccess(user) != PlayAccess.Full)
        {
            return new PreviewContextResolution.Forbidden();
        }

        return await ResolveMediaSourceAsync(query, user, logicalVideo, cancellationToken).ConfigureAwait(false);
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

    private async Task<PreviewContextResolution> ResolveMediaSourceAsync(
        PreviewQuery query,
        User user,
        Video logicalVideo,
        CancellationToken cancellationToken)
    {
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
            return Concealed();
        }

        Video? sourceVideo = libraryManager.GetItemById<Video>(query.ResolvedMediaSourceId, user);
        if (sourceVideo?.Id != query.ResolvedMediaSourceId)
        {
            return Concealed();
        }

        TrickplayFrameCalculationResolution calculation = await calculationResolver
            .ResolveAsync(query, matchedSource.VideoStream?.Width)
            .ConfigureAwait(false);
        return calculation switch
        {
            TrickplayFrameCalculationResolution.Selected selected => new PreviewContextResolution.Resolved(
                new PreviewContext(query.ResolvedMediaSourceId, sourceVideo, selected.Metadata, selected.FrameIndex)),
            TrickplayFrameCalculationResolution.NotFound notFound => new PreviewContextResolution.NotFound(
                notFound.Reason),
            _ => throw new InvalidOperationException(
                $"Unknown Trickplay Frame calculation {calculation.GetType().Name}."),
        };
    }

    private static PreviewContextResolution.NotFound Concealed()
    {
        return new PreviewContextResolution.NotFound(PreviewUnavailableReason.Concealed);
    }

    private static bool IsSelectedSource(MediaSourceInfo source, Guid mediaSourceId)
    {
        return Guid.TryParse(source.Id, out Guid candidateId) && candidateId == mediaSourceId;
    }
}
