using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves a Trickplay Frame Probe through Jellyfin's user-independent Item and source APIs.
/// </summary>
internal sealed class JellyfinTrickplayFrameProbeContextResolver : ITrickplayFrameProbeContextResolver
{
    private readonly ILibraryManager libraryManager;
    private readonly IMediaSourceManager mediaSourceManager;
    private readonly ITrickplayFrameCalculationResolver calculationResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinTrickplayFrameProbeContextResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">Resolves Item and Source Video identity without user visibility filtering.</param>
    /// <param name="mediaSourceManager">Enumerates all supported Media Sources without user shaping.</param>
    /// <param name="calculationResolver">Performs the shared resolution and Frame Index calculation.</param>
    public JellyfinTrickplayFrameProbeContextResolver(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        ITrickplayFrameCalculationResolver calculationResolver)
    {
        this.libraryManager = libraryManager;
        this.mediaSourceManager = mediaSourceManager;
        this.calculationResolver = calculationResolver;
    }

    /// <inheritdoc />
    public async Task<TrickplayFrameCalculationResolution> ResolveAsync(
        PreviewQuery query,
        CancellationToken cancellationToken)
    {
        Video? logicalVideo = libraryManager.GetItemById<Video>(query.ItemId);
        if (logicalVideo?.Id != query.ItemId)
        {
            return Concealed();
        }

        IReadOnlyList<MediaSourceInfo> mediaSources = await mediaSourceManager.GetPlaybackMediaSources(
            logicalVideo,
            user: null!,
            allowMediaProbe: false,
            enablePathSubstitution: false,
            cancellationToken).ConfigureAwait(false);
        MediaSourceInfo? matchedSource = mediaSources.FirstOrDefault(
            source => IsSelectedSource(source, query.ResolvedMediaSourceId));
        if (matchedSource is null)
        {
            return Concealed();
        }

        Video? sourceVideo = libraryManager.GetItemById<Video>(query.ResolvedMediaSourceId);
        if (sourceVideo?.Id != query.ResolvedMediaSourceId)
        {
            return Concealed();
        }

        return await calculationResolver.ResolveAsync(query, matchedSource.VideoStream?.Width).ConfigureAwait(false);
    }

    private static TrickplayFrameCalculationResolution.NotFound Concealed()
    {
        return new TrickplayFrameCalculationResolution.NotFound(PreviewUnavailableReason.Concealed);
    }

    private static bool IsSelectedSource(MediaSourceInfo source, Guid mediaSourceId)
    {
        return Guid.TryParse(source.Id, out Guid candidateId) && candidateId == mediaSourceId;
    }
}
