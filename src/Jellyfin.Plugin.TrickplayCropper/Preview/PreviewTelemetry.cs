using Jellyfin.Plugin.TrickplayCropper.Caching;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Carries timings and cache disposition for a Trickplay Preview response.
/// </summary>
/// <param name="Lookup">The source-resolution duration.</param>
/// <param name="Cache">The optional cache-operation duration.</param>
/// <param name="Decode">The optional Source Sprite decode duration.</param>
/// <param name="Encode">The optional JPEG encode duration.</param>
/// <param name="CacheDisposition">The optional cache disposition.</param>
public sealed record PreviewTelemetry(
    TimeSpan Lookup,
    TimeSpan? Cache,
    TimeSpan? Decode,
    TimeSpan? Encode,
    PreviewCacheDisposition? CacheDisposition);
