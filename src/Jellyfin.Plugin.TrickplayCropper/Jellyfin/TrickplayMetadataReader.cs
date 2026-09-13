using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Trickplay;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Reads the current generated Trickplay metadata for one request.
/// </summary>
internal sealed class TrickplayMetadataReader
{
    private readonly ITrickplayManager trickplayManager;

    public TrickplayMetadataReader(ITrickplayManager trickplayManager)
    {
        this.trickplayManager = trickplayManager;
    }

    public Task<TrickplayMetadataResolution> GetForPreviewAsync(
        Guid sourceVideoId,
        int selectedResolution,
        CancellationToken cancellationToken)
    {
        return ReadAsync(sourceVideoId, selectedResolution, allowNonPositiveInterval: true, cancellationToken);
    }

    public Task<TrickplayMetadataResolution> ReadAuthoritativeTimelineAsync(
        Guid sourceVideoId,
        int selectedResolution,
        CancellationToken cancellationToken)
    {
        return ReadAsync(sourceVideoId, selectedResolution, allowNonPositiveInterval: false, cancellationToken);
    }

    private async Task<TrickplayMetadataResolution> ReadAsync(
        Guid sourceVideoId,
        int selectedResolution,
        bool allowNonPositiveInterval,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<int, TrickplayInfo> resolutions = await trickplayManager
            .GetTrickplayResolutions(sourceVideoId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyDictionary<int, TrickplayMetadata> snapshot = resolutions.ToDictionary(
            pair => pair.Key,
            pair => new TrickplayMetadata(
                pair.Value.Width,
                pair.Value.Height,
                pair.Value.Interval,
                pair.Value.TileWidth,
                pair.Value.TileHeight,
                pair.Value.ThumbnailCount));

        if (snapshot.Count == 0)
        {
            return new TrickplayMetadataResolution.NotFound(PreviewUnavailableReason.NoGeneratedMetadata);
        }

        if (!snapshot.TryGetValue(selectedResolution, out TrickplayMetadata? metadata))
        {
            return new TrickplayMetadataResolution.NotFound(PreviewUnavailableReason.SelectedResolutionMissing);
        }

        try
        {
            if (allowNonPositiveInterval)
            {
                metadata.ValidateForPreview();
            }
            else
            {
                metadata.Validate();
            }

            if (metadata.FrameWidth != selectedResolution)
            {
                throw new InvalidTrickplayMetadataException(
                    metadata,
                    "FrameWidthMatchesResolutionKey",
                    metadata.FrameWidth);
            }
        }
        catch (InvalidTrickplayMetadataException failure)
        {
            failure.Configuration = new PreviewConfigurationDiagnostics
            {
                GeneratedKeys = snapshot.Keys.Order().ToArray(),
            };
            throw;
        }

        return new TrickplayMetadataResolution.Available(metadata);
    }
}
