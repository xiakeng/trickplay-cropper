namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Coordinates shared request access and writer-preferred exclusive Cache Tree access.
/// </summary>
internal sealed class CacheTreeLock
{
    private readonly LinkedList<Waiter> waiters = [];
    private readonly object syncRoot = new();
    private readonly Action<PreviewCacheCheckpoint> checkpointObserver;
    private int activeReaders;
    private bool isWriterActive;

    /// <summary>
    /// Initializes a Cache Tree lock without boundary observation.
    /// </summary>
    public CacheTreeLock()
        : this(static _ => { })
    {
    }

    /// <summary>
    /// Initializes a Cache Tree lock whose coordination boundaries can be observed by component tests.
    /// </summary>
    /// <param name="checkpointObserver">Observes deterministic cache coordination boundaries.</param>
    internal CacheTreeLock(Action<PreviewCacheCheckpoint> checkpointObserver)
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
        return AcquireAsync(LeaseKind.Shared, cancellationToken);
    }

    /// <summary>
    /// Acquires an exclusive Cache Tree lease for directory pruning.
    /// </summary>
    /// <param name="cancellationToken">The cleanup cancellation token.</param>
    /// <returns>A lease that releases exclusive ownership when disposed.</returns>
    public ValueTask<IDisposable> AcquireExclusiveAsync(CancellationToken cancellationToken)
    {
        return AcquireAsync(LeaseKind.Exclusive, cancellationToken);
    }

    /// <summary>
    /// Reports a deterministic boundary while preserving Cache Tree ownership inside this collaborator.
    /// </summary>
    /// <param name="checkpoint">The boundary reached by the cache.</param>
    public void Observe(PreviewCacheCheckpoint checkpoint)
    {
        checkpointObserver(checkpoint);
    }

    private ValueTask<IDisposable> AcquireAsync(LeaseKind kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (syncRoot)
        {
            if (CanAcquireImmediately(kind))
            {
                Activate(kind);
                return ValueTask.FromResult<IDisposable>(new Lease(this, kind));
            }

            var waiter = new Waiter(this, kind, cancellationToken);
            waiter.Node = waiters.AddLast(waiter);
            waiter.RegisterCancellation();
            return new ValueTask<IDisposable>(waiter.Task);
        }
    }

    private bool CanAcquireImmediately(LeaseKind kind)
    {
        if (waiters.Count != 0 || isWriterActive)
        {
            return false;
        }

        return kind == LeaseKind.Shared || activeReaders == 0;
    }

    private void Activate(LeaseKind kind)
    {
        if (kind == LeaseKind.Shared)
        {
            activeReaders = checked(activeReaders + 1);
        }
        else
        {
            isWriterActive = true;
        }
    }

    private void Cancel(Waiter waiter)
    {
        lock (syncRoot)
        {
            if (waiter.Node?.List is null)
            {
                return;
            }

            waiters.Remove(waiter.Node);
            waiter.Node = null;
            GrantWaiters();
        }

        waiter.SetCanceled();
    }

    private void Release(LeaseKind kind)
    {
        lock (syncRoot)
        {
            if (kind == LeaseKind.Shared)
            {
                activeReaders--;
            }
            else
            {
                isWriterActive = false;
            }

            GrantWaiters();
        }
    }

    private void GrantWaiters()
    {
        if (isWriterActive || waiters.First is null)
        {
            return;
        }

        if (activeReaders > 0 && waiters.First.Value.Kind == LeaseKind.Exclusive)
        {
            return;
        }

        if (activeReaders == 0 && waiters.First.Value.Kind == LeaseKind.Exclusive)
        {
            GrantFirstWaiter();
            return;
        }

        while (waiters.First is not null && waiters.First.Value.Kind == LeaseKind.Shared)
        {
            GrantFirstWaiter();
        }
    }

    private void GrantFirstWaiter()
    {
        Waiter waiter = waiters.First!.Value;
        waiters.RemoveFirst();
        waiter.Node = null;
        Activate(waiter.Kind);
        waiter.SetGranted(new Lease(waiter.Owner, waiter.Kind));
    }

    private enum LeaseKind
    {
        Shared,
        Exclusive,
    }

    private sealed class Lease : IDisposable
    {
        private readonly LeaseKind kind;
        private readonly CacheTreeLock owner;
        private int isDisposed;

        public Lease(CacheTreeLock owner, LeaseKind kind)
        {
            this.owner = owner;
            this.kind = kind;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref isDisposed, 1) == 0)
            {
                owner.Release(kind);
            }
        }
    }

    private sealed class Waiter
    {
        private readonly CancellationToken cancellationToken;
        private readonly TaskCompletionSource<IDisposable> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration cancellationRegistration;
        private bool isCompleted;

        public Waiter(CacheTreeLock owner, LeaseKind kind, CancellationToken cancellationToken)
        {
            Owner = owner;
            Kind = kind;
            this.cancellationToken = cancellationToken;
        }

        public LeaseKind Kind { get; }

        public LinkedListNode<Waiter>? Node { get; set; }

        public CacheTreeLock Owner { get; }

        public Task<IDisposable> Task => completion.Task;

        public void RegisterCancellation()
        {
            CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((Waiter)state!).CancelWaiting(),
                this);
            cancellationRegistration = registration;
            if (isCompleted)
            {
                registration.Dispose();
            }
        }

        public void SetCanceled()
        {
            isCompleted = true;
            _ = cancellationRegistration.Unregister();
            completion.TrySetCanceled(cancellationToken);
        }

        public void SetGranted(IDisposable lease)
        {
            isCompleted = true;
            _ = cancellationRegistration.Unregister();
            completion.TrySetResult(lease);
        }

        private void CancelWaiting()
        {
            Owner.Cancel(this);
        }
    }
}
