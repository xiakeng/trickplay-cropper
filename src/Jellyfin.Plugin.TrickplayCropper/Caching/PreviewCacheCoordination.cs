namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Owns process-local Cache Tree leases and deterministic Preview Cache Entry boundaries.
/// </summary>
internal sealed class PreviewCacheCoordination
{
    private readonly Action<PreviewCacheCheckpoint> checkpointObserver;
    private readonly CacheTreeLock treeLock = new();

    /// <summary>
    /// Initializes process-local coordination without boundary observation.
    /// </summary>
    public PreviewCacheCoordination()
        : this(static _ => { })
    {
    }

    /// <summary>
    /// Initializes process-local coordination whose boundaries can be observed by component tests.
    /// </summary>
    /// <param name="checkpointObserver">Observes deterministic cache coordination boundaries.</param>
    internal PreviewCacheCoordination(Action<PreviewCacheCheckpoint> checkpointObserver)
    {
        this.checkpointObserver = checkpointObserver;
    }

    /// <summary>
    /// Acquires a shared Cache Tree lease for a request.
    /// </summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A lease that releases shared ownership when disposed.</returns>
    public ValueTask<IDisposable> AcquireSharedAsync(CancellationToken cancellationToken)
    {
        return treeLock.AcquireSharedAsync(cancellationToken);
    }

    /// <summary>
    /// Acquires an exclusive Cache Tree lease for directory pruning.
    /// </summary>
    /// <param name="cancellationToken">The cleanup cancellation token.</param>
    /// <returns>A lease that releases exclusive ownership when disposed.</returns>
    public ValueTask<IDisposable> AcquireExclusiveAsync(CancellationToken cancellationToken)
    {
        return treeLock.AcquireExclusiveAsync(cancellationToken);
    }

    /// <summary>
    /// Reports a deterministic Preview Cache Entry boundary.
    /// </summary>
    /// <param name="checkpoint">The boundary reached by the cache.</param>
    public void Observe(PreviewCacheCheckpoint checkpoint)
    {
        checkpointObserver(checkpoint);
    }
}
