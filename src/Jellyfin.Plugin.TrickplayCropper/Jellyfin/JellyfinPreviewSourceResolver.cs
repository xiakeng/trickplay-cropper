using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves the GET-only Source Sprite snapshot through Jellyfin-owned managers.
/// </summary>
internal sealed class JellyfinPreviewSourceResolver : IPreviewSourceResolver
{
    private readonly ILibraryManager libraryManager;
    private readonly ITrickplayManager trickplayManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinPreviewSourceResolver"/> class.
    /// </summary>
    public JellyfinPreviewSourceResolver(
        ILibraryManager libraryManager,
        ITrickplayManager trickplayManager)
    {
        this.libraryManager = libraryManager;
        this.trickplayManager = trickplayManager;
    }

    /// <inheritdoc />
    public async Task<PreviewSourceResolution> ResolveAsync(PreviewContext context)
    {
        TrickplayMetadata metadata = context.Metadata;
        FrameSelection selection = FrameSelection.Create(metadata, context.FrameIndex);
        long? sourceLength = null;
        long? sourceLastWriteUtcTicks = null;
        try
        {
            LibraryOptions libraryOptions = libraryManager.GetLibraryOptions(context.SourceVideo);
            string sourceSpritePath = await trickplayManager.GetTrickplayTilePathAsync(
                context.SourceVideo,
                metadata.FrameWidth,
                selection.SpriteIndex,
                libraryOptions.SaveTrickplayWithMedia).ConfigureAwait(false);
            if (!File.Exists(sourceSpritePath))
            {
                return new PreviewSourceResolution.NotFound();
            }

            var sourceSprite = new FileInfo(sourceSpritePath);
            sourceLength = sourceSprite.Length;
            sourceLastWriteUtcTicks = sourceSprite.LastWriteTimeUtc.Ticks;
            return new PreviewSourceResolution.Found(
                new ResolvedPreviewSource(
                    context.MediaSourceId,
                    sourceSpritePath,
                    sourceLength.Value,
                    sourceLastWriteUtcTicks.Value,
                    metadata,
                    selection));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var details = new PreviewFailureDetails
            {
                Metadata = metadata,
                Selection = selection,
                SourceLength = sourceLength,
                SourceLastWriteUtcTicks = sourceLastWriteUtcTicks,
            };
            throw new PreviewStageException(exception, details);
        }
    }
}
