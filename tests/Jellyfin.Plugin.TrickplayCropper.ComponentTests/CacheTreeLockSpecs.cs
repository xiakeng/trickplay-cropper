using Jellyfin.Plugin.TrickplayCropper.Caching;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class CacheTreeLockSpecs
{
    private static readonly TimeSpan coordinationTimeout = TimeSpan.FromSeconds(10);

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
        IDisposable exclusiveLease = await exclusiveLeaseTask.WaitAsync(coordinationTimeout);
        Assert.False(secondSharedLeaseTask.IsCompleted);

        exclusiveLease.Dispose();
        using IDisposable secondSharedLease = await secondSharedLeaseTask.WaitAsync(coordinationTimeout);
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
        using IDisposable exclusiveLease = await exclusiveLeaseTask.WaitAsync(coordinationTimeout);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => exclusiveLeaseTask.WaitAsync(coordinationTimeout));
        using IDisposable secondSharedLease = await secondSharedLeaseTask.WaitAsync(coordinationTimeout);
    }

    [Fact]
    public async Task CancelledSharedWaiterDoesNotLeakReaderOwnership()
    {
        var treeLock = new CacheTreeLock();
        IDisposable exclusiveLease = await treeLock.AcquireExclusiveAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        Task<IDisposable> cancelledSharedLeaseTask = treeLock
            .AcquireSharedAsync(cancellation.Token)
            .AsTask();
        Task<IDisposable> followingSharedLeaseTask = treeLock
            .AcquireSharedAsync(CancellationToken.None)
            .AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelledSharedLeaseTask.WaitAsync(coordinationTimeout));
        exclusiveLease.Dispose();
        IDisposable followingSharedLease = await followingSharedLeaseTask.WaitAsync(coordinationTimeout);
        followingSharedLease.Dispose();
        using IDisposable finalExclusiveLease = await treeLock
            .AcquireExclusiveAsync(CancellationToken.None)
            .AsTask()
            .WaitAsync(coordinationTimeout);
    }
}
