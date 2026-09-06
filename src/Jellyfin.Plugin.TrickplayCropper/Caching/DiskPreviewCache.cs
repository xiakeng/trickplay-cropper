using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Stores Preview Cache Entries beneath Jellyfin temporary storage.
/// </summary>
internal sealed class DiskPreviewCache : IPreviewCache, IDisposable
{
    private readonly DiskPreviewCacheCleanup cleanup;
    private readonly DiskPreviewCacheEntryStore entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskPreviewCache"/> class.
    /// </summary>
    public DiskPreviewCache(
        IApplicationPaths applicationPaths,
        TimeProvider timeProvider,
        ILogger<DiskPreviewCache> logger)
        : this(
            applicationPaths,
            timeProvider,
            new PreviewCacheCoordination(logger),
            logger)
    {
    }

    /// <summary>
    /// Initializes a cache with an explicit process-local coordination collaborator.
    /// </summary>
    /// <param name="applicationPaths">The Jellyfin application paths.</param>
    /// <param name="timeProvider">The source of cleanup time.</param>
    /// <param name="coordination">Coordinates Cache Tree ownership and deterministic boundaries.</param>
    internal DiskPreviewCache(
        IApplicationPaths applicationPaths,
        TimeProvider timeProvider,
        PreviewCacheCoordination coordination,
        ILogger<DiskPreviewCache> logger)
    {
        var paths = new PreviewCachePaths(applicationPaths.TempDirectory, coordination.PathComparison);
        entries = new DiskPreviewCacheEntryStore(paths, coordination);
        cleanup = new DiskPreviewCacheCleanup(paths, timeProvider, coordination, logger);
    }

    /// <inheritdoc />
    public Task<PreviewCacheResult> GetOrCreateAsync(
        PreviewIdentity identity,
        Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
        CancellationToken cancellationToken)
    {
        return entries.GetOrCreateAsync(identity, writer, cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return cleanup.ClearAsync(progress, cancellationToken);
    }

    /// <summary>
    /// Releases process-local cleanup resources.
    /// </summary>
    public void Dispose()
    {
        cleanup.Dispose();
    }
}
