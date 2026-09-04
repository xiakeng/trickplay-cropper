using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Library;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves the shared Preview context through Jellyfin-owned managers.
/// </summary>
internal sealed class JellyfinPreviewContextResolver : IPreviewContextResolver
{
    private const string JellyfinIsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string JellyfinUserIdClaim = "Jellyfin-UserId";
    private const int PreviewWidth = 320;

    private readonly IUserManager userManager;
    private readonly ILibraryManager libraryManager;
    private readonly IMediaSourceManager mediaSourceManager;
    private readonly ITrickplayManager trickplayManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPreviewContextResolver"/> class.
    /// </summary>
    public JellyfinPreviewContextResolver(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        ITrickplayManager trickplayManager)
    {
        this.userManager = userManager;
        this.libraryManager = libraryManager;
        this.mediaSourceManager = mediaSourceManager;
        this.trickplayManager = trickplayManager;
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
        if (logicalVideo is null)
        {
            return new PreviewContextResolution.NotFound();
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
        Guid mediaSourceId = query.ResolvedMediaSourceId;
        IReadOnlyList<MediaSourceInfo> mediaSources = await mediaSourceManager.GetPlaybackMediaSources(
            logicalVideo,
            user,
            true,
            false,
            cancellationToken).ConfigureAwait(false);
        bool isMember = mediaSources.Any(source => IsSelectedSource(source, mediaSourceId));
        if (!isMember)
        {
            return new PreviewContextResolution.NotFound();
        }

        Video? sourceVideo = libraryManager.GetItemById<Video>(mediaSourceId, user);
        if (sourceVideo is null)
        {
            return new PreviewContextResolution.NotFound();
        }

        if (sourceVideo.GetPlayAccess(user) != PlayAccess.Full)
        {
            return new PreviewContextResolution.Forbidden();
        }

        return await SelectMetadataAsync(query, sourceVideo, mediaSourceId).ConfigureAwait(false);
    }

    private async Task<PreviewContextResolution> SelectMetadataAsync(
        PreviewQuery query,
        Video sourceVideo,
        Guid mediaSourceId)
    {
        Dictionary<int, TrickplayInfo> resolutions = await trickplayManager
            .GetTrickplayResolutions(mediaSourceId)
            .ConfigureAwait(false);
        if (!resolutions.TryGetValue(PreviewWidth, out TrickplayInfo? info) || info.ThumbnailCount <= 0)
        {
            return new PreviewContextResolution.NotFound();
        }

        TrickplayMetadata metadata = CreateMetadata(info);
        metadata.Validate();
        if (metadata.FrameWidth != PreviewWidth)
        {
            throw new InvalidTrickplayMetadataException(
                metadata,
                "FrameWidthMatchesResolutionKey",
                metadata.FrameWidth);
        }

        return new PreviewContextResolution.Resolved(
            new PreviewContext(mediaSourceId, sourceVideo, metadata, metadata.SelectFrameIndex(query.PositionTicks)));
    }

    private static bool IsSelectedSource(MediaSourceInfo source, Guid mediaSourceId)
    {
        return Guid.TryParse(source.Id, out Guid candidateId) && candidateId == mediaSourceId;
    }

    private static TrickplayMetadata CreateMetadata(TrickplayInfo info)
    {
        return new TrickplayMetadata(
            info.Width,
            info.Height,
            info.Interval,
            info.TileWidth,
            info.TileHeight,
            info.ThumbnailCount);
    }
}
