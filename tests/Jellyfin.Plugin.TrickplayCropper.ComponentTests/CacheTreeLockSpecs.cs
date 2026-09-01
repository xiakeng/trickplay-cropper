using Jellyfin.Plugin.TrickplayCropper.Caching;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class CacheTreeLockSpecs
{
    [Fact]
    public async Task QueuedExclusiveLeaseBlocksNewSharedLeases()
    {
        var treeLock = new CacheTreeLock();
        IDisposable firstSharedLease = await treeLock.AcquireSharedAsync(CancellationToken.None);
        Task<IDisposable> exclusiveLeaseTask = treeLock
            .AcquireExclusiveAsync(CancellationToken.None)
            .AsTask();
        Task<IDisposable> secondSharedLeaseTask = treeLock
            .AcquireSharedAsync(CancellationToken.None)
            .AsTask();

        Assert.False(exclusiveLeaseTask.IsCompleted);
        Assert.False(secondSharedLeaseTask.IsCompleted);

        firstSharedLease.Dispose();
        IDisposable exclusiveLease = await exclusiveLeaseTask;
        Assert.False(secondSharedLeaseTask.IsCompleted);

        exclusiveLease.Dispose();
        using IDisposable secondSharedLease = await secondSharedLeaseTask;
    }

    [Fact]
    public async Task ExclusiveLeaseWaitsForEverySharedLease()
    {
        var treeLock = new CacheTreeLock();
        IDisposable firstSharedLease = await treeLock.AcquireSharedAsync(CancellationToken.None);
        IDisposable secondSharedLease = await treeLock.AcquireSharedAsync(CancellationToken.None);
        Task<IDisposable> exclusiveLeaseTask = treeLock
            .AcquireExclusiveAsync(CancellationToken.None)
            .AsTask();

        firstSharedLease.Dispose();
        Assert.False(exclusiveLeaseTask.IsCompleted);

        secondSharedLease.Dispose();
        using IDisposable exclusiveLease = await exclusiveLeaseTask;
    }

    [Fact]
    public async Task CancelledExclusiveWaiterDoesNotBlockSharedLeases()
    {
        var treeLock = new CacheTreeLock();
        using IDisposable firstSharedLease = await treeLock.AcquireSharedAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        Task<IDisposable> exclusiveLeaseTask = treeLock
            .AcquireExclusiveAsync(cancellation.Token)
            .AsTask();
        Task<IDisposable> secondSharedLeaseTask = treeLock
            .AcquireSharedAsync(CancellationToken.None)
            .AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exclusiveLeaseTask);
        using IDisposable secondSharedLease = await secondSharedLeaseTask;
    }
}
