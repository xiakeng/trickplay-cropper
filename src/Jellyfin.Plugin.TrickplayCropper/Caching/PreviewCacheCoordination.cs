namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Owns process-local Cache Tree leases and deterministic Preview Cache Entry boundaries.
/// </summary>
internal sealed class PreviewCacheCoordination
{
    private readonly Action<PreviewCacheCheckpoint> checkpointObserver;
    private readonly PreviewEntryLockRegistry entryLocks;
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
        StringComparer pathComparer;
        if (OperatingSystem.IsWindows())
        {
            pathComparer = StringComparer.OrdinalIgnoreCase;
            PathComparison = StringComparison.OrdinalIgnoreCase;
        }
        else
        {
            pathComparer = StringComparer.Ordinal;
            PathComparison = StringComparison.Ordinal;
        }

        entryLocks = new PreviewEntryLockRegistry(pathComparer);
    }

    /// <summary>
    /// Gets the platform path comparison shared by Cache Tree containment and keyed entry identity.
    /// </summary>
    public StringComparison PathComparison { get; }

    /// <summary>
    /// Executes one Preview Cache Entry operation under the required Cache Tree and entry lease order.
    /// </summary>
    /// <typeparam name="TResult">The immutable buffered operation result.</typeparam>
    /// <param name="path">The canonical absolute final Preview Cache Entry path.</param>
    /// <param name="operation">The operation that reads or generates and buffers the entry.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The result produced before both leases are released.</returns>
    public async Task<TResult> ExecuteEntryAsync<TResult>(
        string path,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        using IDisposable treeLease = await treeLock
            .AcquireSharedAsync(cancellationToken)
            .ConfigureAwait(false);
        checkpointObserver(PreviewCacheCheckpoint.TreeLeaseAcquired);
        using IDisposable entryLease = await entryLocks
            .AcquireAsync(path, cancellationToken)
            .ConfigureAwait(false);
        checkpointObserver(PreviewCacheCheckpoint.EntryLeaseAcquired);
        TResult result = await operation(cancellationToken).ConfigureAwait(false);
        checkpointObserver(PreviewCacheCheckpoint.ResponseBuffered);
        return result;
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
    /// Reports the final-path recheck boundary immediately before publication.
    /// </summary>
    public void ObserveBeforePublication()
    {
        checkpointObserver(PreviewCacheCheckpoint.BeforePublication);
    }

    /// <summary>
    /// Reports the boundary immediately after this process publishes a Preview Cache Entry.
    /// </summary>
    public void ObserveAfterPublication()
    {
        checkpointObserver(PreviewCacheCheckpoint.AfterPublication);
    }
}
