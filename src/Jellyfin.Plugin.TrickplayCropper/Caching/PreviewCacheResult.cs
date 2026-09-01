using Jellyfin.Plugin.TrickplayCropper.Imaging;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Contains immutable response bytes and cache-generation details.
/// </summary>
/// <param name="Content">The buffered Preview Cache Entry.</param>
/// <param name="Disposition">The cache disposition.</param>
/// <param name="EncodingTelemetry">The optional generation telemetry.</param>
internal sealed record PreviewCacheResult(
    ReadOnlyMemory<byte> Content,
    PreviewCacheDisposition Disposition,
    PreviewEncodingTelemetry? EncodingTelemetry);
