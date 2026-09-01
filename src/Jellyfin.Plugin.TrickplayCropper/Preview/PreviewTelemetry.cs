using Jellyfin.Plugin.TrickplayCropper.Caching;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Carries the stages completed for a Trickplay Preview response.
/// </summary>
/// <param name="Lookup">The source-resolution duration.</param>
/// <param name="Cache">The optional cache-operation duration.</param>
/// <param name="Decode">The optional Source Sprite decode duration.</param>
/// <param name="Encode">The optional JPEG encode duration.</param>
public abstract record PreviewTelemetry(
    TimeSpan Lookup,
    TimeSpan? Cache,
    TimeSpan? Decode,
    TimeSpan? Encode)
{
    /// <summary>
    /// Carries stages and disposition for a response that accessed the Preview Cache.
    /// </summary>
    public sealed record CacheAccess : PreviewTelemetry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CacheAccess"/> record.
        /// </summary>
        /// <param name="lookup">The source-resolution duration.</param>
        /// <param name="cacheDuration">The cache-operation duration.</param>
        /// <param name="cacheResult">The immutable cache result and any completed encoding stages.</param>
        internal CacheAccess(
            TimeSpan lookup,
            TimeSpan cacheDuration,
            PreviewCacheResult cacheResult)
            : base(
                lookup,
                cacheDuration,
                cacheResult.EncodingTelemetry?.Decode,
                cacheResult.EncodingTelemetry?.Encode)
        {
            CacheDisposition = cacheResult.Disposition;
        }

        /// <summary>
        /// Gets the representation served from the Preview Cache.
        /// </summary>
        public PreviewCacheDisposition CacheDisposition { get; }
    }

    /// <summary>
    /// Carries the source-resolution stage for a conditional response that avoided the Preview Cache.
    /// </summary>
    /// <param name="Lookup">The source-resolution duration.</param>
    public sealed record Conditional(TimeSpan Lookup) : PreviewTelemetry(Lookup, null, null, null);
}
