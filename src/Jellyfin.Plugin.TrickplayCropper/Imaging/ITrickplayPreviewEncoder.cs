using Jellyfin.Plugin.TrickplayCropper.Jellyfin;

namespace Jellyfin.Plugin.TrickplayCropper.Imaging;

/// <summary>
/// Crops and encodes one frame from a resolved JPEG Source Sprite.
/// </summary>
internal interface ITrickplayPreviewEncoder
{
    /// <summary>
    /// Encodes the selected frame directly into a cache-owned stream.
    /// </summary>
    /// <param name="source">The resolved Source Sprite and crop.</param>
    /// <param name="destination">The cache-owned destination stream.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The decode and encode timings.</returns>
    Task<PreviewEncodingTelemetry> EncodeAsync(
        ResolvedPreviewSource source,
        Stream destination,
        CancellationToken cancellationToken);
}
