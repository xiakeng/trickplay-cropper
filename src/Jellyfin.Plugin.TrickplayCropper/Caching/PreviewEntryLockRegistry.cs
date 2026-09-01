namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Coordinates exclusive process-local ownership of each Preview Cache Entry path.
/// </summary>
internal sealed class PreviewEntryLockRegistry
{
    private readonly Dictionary<string, EntryLock> entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreviewEntryLockRegistry"/> class.
    /// </summary>
    /// <param name="pathComparer">The platform path-identity comparer.</param>
    public PreviewEntryLockRegistry(StringComparer pathComparer)
    {
        entries = new Dictionary<string, EntryLock>(pathComparer);
    }

    /// <summary>
    /// Gets the number of Preview Cache Entry locks with active owners or waiters.
    /// </summary>
    public int EntryCount
    {
        get
        {
            lock (entries)
            {
                return entries.Count;
            }
        }
    }

    /// <summary>
    /// Acquires exclusive ownership of one canonical Preview Cache Entry path.
    /// </summary>
    /// <param name="path">The canonical absolute final path.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>An ownership lease that releases the entry when disposed.</returns>
    public async ValueTask<IDisposable> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        EntryLock entry = AddReference(path);
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new EntryLease(this, path, entry);
        }
        catch
        {
            ReleaseReference(path, entry);
            throw;
        }
    }

    private EntryLock AddReference(string path)
    {
        lock (entries)
        {
            if (!entries.TryGetValue(path, out EntryLock? entry))
            {
                entry = new EntryLock();
                entries.Add(path, entry);
            }

            entry.ReferenceCount = checked(entry.ReferenceCount + 1);
            return entry;
        }
    }

    private void Release(string path, EntryLock entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(path, entry);
    }

    private void ReleaseReference(string path, EntryLock entry)
    {
        bool disposeEntry = false;
        lock (entries)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                if (!entries.TryGetValue(path, out EntryLock? current)
                    || !ReferenceEquals(entry, current)
                    || !entries.Remove(path))
                {
                    throw new InvalidOperationException("The idle Preview Cache Entry lock was not current.");
                }

                disposeEntry = true;
            }
        }

        if (disposeEntry)
        {
            entry.Semaphore.Dispose();
        }
    }

    private sealed class EntryLease : IDisposable
    {
        private readonly EntryLock entry;
        private readonly string path;
        private readonly PreviewEntryLockRegistry registry;
        private int isDisposed;

        public EntryLease(PreviewEntryLockRegistry registry, string path, EntryLock entry)
        {
            this.registry = registry;
            this.path = path;
            this.entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref isDisposed, 1) == 0)
            {
                registry.Release(path, entry);
            }
        }
    }

    private sealed class EntryLock
    {
        public int ReferenceCount { get; set; }

        public SemaphoreSlim Semaphore { get; } = new(1, 1);
    }
}
