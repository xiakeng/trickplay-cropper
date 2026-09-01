using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Owns Preview Cache Entry storage and response buffering.
/// </summary>
internal interface IPreviewCache : IPreviewCacheMaintenance
{
    /// <summary>
    /// Reads or generates one immutable Preview Cache Entry.
    /// </summary>
    /// <param name="identity">The canonical entry identity.</param>
    /// <param name="writer">The callback that writes a generated JPEG.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The buffered cache result and generation telemetry.</returns>
    Task<PreviewCacheResult> GetOrCreateAsync(
        PreviewIdentity identity,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken);

}
