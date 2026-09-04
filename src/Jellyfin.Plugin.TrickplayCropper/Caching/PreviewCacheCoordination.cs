using Jellyfin.Plugin.TrickplayCropper.Preview;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Owns process-local Cache Tree leases and deterministic Preview Cache Entry boundaries.
/// </summary>
internal sealed class PreviewCacheCoordination
{
    private readonly Action<PreviewCacheCheckpoint> checkpointObserver;
    private readonly PreviewEntryLockRegistry entryLocks;
    private readonly ILogger logger;
    private readonly CacheTreeLock treeLock = new();

    /// <summary>
    /// Initializes process-local coordination without boundary observation.
    /// </summary>
    /// <param name="logger">The category logger that reports coordination waits and ownership.</param>
    public PreviewCacheCoordination(ILogger logger)
        : this(logger, static _ => { })
    {
    }

    /// <summary>
    /// Initializes process-local coordination whose boundaries can be observed by component tests.
    /// </summary>
    /// <param name="logger">The category logger that reports coordination waits and ownership.</param>
    /// <param name="checkpointObserver">Observes deterministic cache coordination boundaries.</param>
    internal PreviewCacheCoordination(ILogger logger, Action<PreviewCacheCheckpoint> checkpointObserver)
    {
        this.logger = logger;
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
        // A pending acquisition queued a waiter; a completed one acquired immediately, failed, or was canceled.
        ValueTask<IDisposable> treeLeaseAcquisition = treeLock.AcquireSharedAsync(cancellationToken);
        if (!treeLeaseAcquisition.IsCompleted)
        {
            PreviewDebugProtocol.LogCacheTreeLeaseWaiting(logger);
        }

        using IDisposable treeLease = await treeLeaseAcquisition.ConfigureAwait(false);
        checkpointObserver(PreviewCacheCheckpoint.TreeLeaseAcquired);
        ValueTask<IDisposable> entryLeaseAcquisition = entryLocks.AcquireAsync(path, cancellationToken);
        if (!entryLeaseAcquisition.IsCompleted)
        {
            PreviewDebugProtocol.LogEntryLockWaiting(logger);
        }

        using IDisposable entryLease = await entryLeaseAcquisition.ConfigureAwait(false);
        PreviewDebugProtocol.LogEntryLockOwned(logger);
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
        ValueTask<IDisposable> leaseAcquisition = treeLock.AcquireExclusiveAsync(cancellationToken);
        if (!leaseAcquisition.IsCompleted)
        {
            PreviewDebugProtocol.LogCacheTreeLeaseWaiting(logger);
        }

        return leaseAcquisition;
    }

    /// <summary>
    /// Executes one indivisible cleanup operation under shared Cache Tree and entry ownership.
    /// </summary>
    /// <param name="path">The canonical final Preview Cache Entry path used as the lock key.</param>
    /// <param name="operation">The indivisible filesystem operation.</param>
    /// <param name="cancellationToken">The cleanup cancellation token.</param>
    /// <returns>A task representing cleanup ownership and execution.</returns>
    public async Task ExecuteCleanupEntryAsync(
        string path,
        Action operation,
        CancellationToken cancellationToken)
    {
        ValueTask<IDisposable> treeLeaseAcquisition = treeLock.AcquireSharedAsync(cancellationToken);
        if (!treeLeaseAcquisition.IsCompleted)
        {
            PreviewDebugProtocol.LogCacheTreeLeaseWaiting(logger);
        }

        using IDisposable treeLease = await treeLeaseAcquisition.ConfigureAwait(false);
        ValueTask<IDisposable> entryLeaseAcquisition = entryLocks.AcquireAsync(path, cancellationToken);
        if (!entryLeaseAcquisition.IsCompleted)
        {
            PreviewDebugProtocol.LogEntryLockWaiting(logger);
        }

        using IDisposable entryLease = await entryLeaseAcquisition.ConfigureAwait(false);
        PreviewDebugProtocol.LogEntryLockOwned(logger);
        checkpointObserver(PreviewCacheCheckpoint.CleanupEntryLeaseAcquired);
        cancellationToken.ThrowIfCancellationRequested();
        operation();
    }

    /// <summary>
    /// Reports that a cleanup invocation is about to request the single-run mutex.
    /// </summary>
    public void ObserveCleanupRunRequested()
    {
        checkpointObserver(PreviewCacheCheckpoint.CleanupRunRequested);
    }

    /// <summary>
    /// Reports that cleanup owns the single-run mutex and has captured its fixed boundary.
    /// </summary>
    public void ObserveCleanupStarted()
    {
        checkpointObserver(PreviewCacheCheckpoint.CleanupStarted);
    }

    /// <summary>
    /// Reports that cleanup discovered a filesystem entry before inspecting its attributes.
    /// </summary>
    public void ObserveCleanupEntryDiscovered()
    {
        checkpointObserver(PreviewCacheCheckpoint.CleanupEntryDiscovered);
    }

    /// <summary>
    /// Reports that cleanup captured a candidate fingerprint before requesting entry ownership.
    /// </summary>
    public void ObserveCleanupCandidateCaptured()
    {
        checkpointObserver(PreviewCacheCheckpoint.CleanupCandidateCaptured);
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
