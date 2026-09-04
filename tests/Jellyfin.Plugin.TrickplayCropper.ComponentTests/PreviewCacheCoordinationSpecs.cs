using Jellyfin.Plugin.TrickplayCropper.Caching;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class PreviewCacheCoordinationSpecs
{
    private const string EntryPath = "/cache/preview-v1/f0000000000.jpg";
    private static readonly TimeSpan coordinationTimeout = TimeSpan.FromSeconds(10);
    private static readonly EventId entryLockWaiting = new(1004, "TrickplayPreviewEntryLockWaiting");
    private static readonly EventId entryLockOwned = new(1005, "TrickplayPreviewEntryLockOwned");
    private static readonly EventId cacheTreeLeaseWaiting = new(1006, "TrickplayPreviewCacheTreeLeaseWaiting");

    [Fact]
    public async Task ReportsEntryOwnershipWithoutAnEntryLockWaitForAnUncontendedEntry()
    {
        var logger = new DebugProtocolLogger<PreviewCacheCoordination>();
        var coordination = new PreviewCacheCoordination(logger);

        int result = await coordination.ExecuteEntryAsync(
            EntryPath,
            static _ => Task.FromResult(1),
            CancellationToken.None);

        Assert.Equal(1, result);
        Assert.Single(logger.Events, recorded => recorded.EventId == entryLockOwned);
        Assert.DoesNotContain(logger.Events, recorded => recorded.EventId == entryLockWaiting);
        Assert.DoesNotContain(logger.Events, recorded => recorded.EventId == cacheTreeLeaseWaiting);
    }

    [Fact]
    public async Task ReportsEntryLockWaitingBehindTheCurrentEntryOwner()
    {
        var logger = new DebugProtocolLogger<PreviewCacheCoordination>();
        var coordination = new PreviewCacheCoordination(logger);
        var ownerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOwner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> owner = coordination.ExecuteEntryAsync(
            EntryPath,
            async _ =>
            {
                ownerStarted.SetResult();
                await releaseOwner.Task.ConfigureAwait(false);
                return 1;
            },
            CancellationToken.None);
        await ownerStarted.Task.WaitAsync(coordinationTimeout);

        Task<int> waiter = coordination.ExecuteEntryAsync(
            EntryPath,
            static _ => Task.FromResult(2),
            CancellationToken.None);

        Assert.False(waiter.IsCompleted);
        Assert.Single(logger.Events, recorded => recorded.EventId == entryLockWaiting);
        Assert.Single(logger.Events, recorded => recorded.EventId == entryLockOwned);

        releaseOwner.SetResult();
        Assert.Equal(1, await owner.WaitAsync(coordinationTimeout));
        Assert.Equal(2, await waiter.WaitAsync(coordinationTimeout));
        Assert.Single(logger.Events, recorded => recorded.EventId == entryLockWaiting);
        Assert.Equal(2, logger.Events.Count(recorded => recorded.EventId == entryLockOwned));
        Assert.DoesNotContain(logger.Events, recorded => recorded.EventId == cacheTreeLeaseWaiting);
    }

    [Fact]
    public async Task ReportsCacheTreeLeaseWaitingBehindAnExclusiveLease()
    {
        var logger = new DebugProtocolLogger<PreviewCacheCoordination>();
        var coordination = new PreviewCacheCoordination(logger);
        using IDisposable exclusiveLease = await coordination.AcquireExclusiveAsync(CancellationToken.None);

        Task<int> request = coordination.ExecuteEntryAsync(
            EntryPath,
            static _ => Task.FromResult(1),
            CancellationToken.None);

        Assert.False(request.IsCompleted);
        Assert.Single(logger.Events, recorded => recorded.EventId == cacheTreeLeaseWaiting);
        Assert.DoesNotContain(logger.Events, recorded => recorded.EventId == entryLockWaiting);
        Assert.DoesNotContain(logger.Events, recorded => recorded.EventId == entryLockOwned);

        exclusiveLease.Dispose();
        Assert.Equal(1, await request.WaitAsync(coordinationTimeout));
        Assert.Single(logger.Events, recorded => recorded.EventId == cacheTreeLeaseWaiting);
        Assert.Single(logger.Events, recorded => recorded.EventId == entryLockOwned);
    }

    [Fact]
    public async Task ReportsCacheTreeLeaseWaitingForAnExclusiveLeaseBehindARequest()
    {
        var logger = new DebugProtocolLogger<PreviewCacheCoordination>();
        var coordination = new PreviewCacheCoordination(logger);
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> request = coordination.ExecuteEntryAsync(
            EntryPath,
            async _ =>
            {
                requestStarted.SetResult();
                await releaseRequest.Task.ConfigureAwait(false);
                return 1;
            },
            CancellationToken.None);
        await requestStarted.Task.WaitAsync(coordinationTimeout);

        ValueTask<IDisposable> cleanup = coordination.AcquireExclusiveAsync(CancellationToken.None);

        Assert.False(cleanup.IsCompleted);
        Assert.Single(logger.Events, recorded => recorded.EventId == cacheTreeLeaseWaiting);

        releaseRequest.SetResult();
        Assert.Equal(1, await request.WaitAsync(coordinationTimeout));
        using IDisposable exclusiveLease = await cleanup.AsTask().WaitAsync(coordinationTimeout);
        Assert.Single(logger.Events, recorded => recorded.EventId == cacheTreeLeaseWaiting);
    }

    [Fact]
    public async Task ReportsCleanupEntryLockWaitingBehindTheCurrentEntryOwner()
    {
        var logger = new DebugProtocolLogger<PreviewCacheCoordination>();
        var coordination = new PreviewCacheCoordination(logger);
        var ownerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOwner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> owner = coordination.ExecuteEntryAsync(
            EntryPath,
            async _ =>
            {
                ownerStarted.SetResult();
                await releaseOwner.Task.ConfigureAwait(false);
                return 1;
            },
            CancellationToken.None);
        await ownerStarted.Task.WaitAsync(coordinationTimeout);

        bool cleanupRan = false;
        Task cleanup = coordination.ExecuteCleanupEntryAsync(
            EntryPath,
            () => cleanupRan = true,
            CancellationToken.None);

        Assert.False(cleanup.IsCompleted);
        Assert.False(cleanupRan);
        Assert.Single(logger.Events, recorded => recorded.EventId == entryLockWaiting);
        Assert.Single(logger.Events, recorded => recorded.EventId == entryLockOwned);

        releaseOwner.SetResult();
        Assert.Equal(1, await owner.WaitAsync(coordinationTimeout));
        await cleanup.WaitAsync(coordinationTimeout);

        Assert.True(cleanupRan);
        Assert.Single(logger.Events, recorded => recorded.EventId == entryLockWaiting);
        Assert.Equal(2, logger.Events.Count(recorded => recorded.EventId == entryLockOwned));
    }

    [Fact]
    public async Task ReportsCleanupCacheTreeLeaseWaitingBehindAnExclusiveLease()
    {
        var logger = new DebugProtocolLogger<PreviewCacheCoordination>();
        var coordination = new PreviewCacheCoordination(logger);
        using IDisposable exclusiveLease = await coordination.AcquireExclusiveAsync(CancellationToken.None);

        bool cleanupRan = false;
        Task cleanup = coordination.ExecuteCleanupEntryAsync(
            EntryPath,
            () => cleanupRan = true,
            CancellationToken.None);

        Assert.False(cleanup.IsCompleted);
        Assert.False(cleanupRan);
        Assert.Single(logger.Events, recorded => recorded.EventId == cacheTreeLeaseWaiting);
        Assert.DoesNotContain(logger.Events, recorded => recorded.EventId == entryLockOwned);

        exclusiveLease.Dispose();
        await cleanup.WaitAsync(coordinationTimeout);

        Assert.True(cleanupRan);
        Assert.Single(logger.Events, recorded => recorded.EventId == cacheTreeLeaseWaiting);
        Assert.Single(logger.Events, recorded => recorded.EventId == entryLockOwned);
        Assert.DoesNotContain(logger.Events, recorded => recorded.EventId == entryLockWaiting);
    }

    [Fact]
    public async Task CarriesNoFieldsBeyondTheStableMessageTemplate()
    {
        var logger = new DebugProtocolLogger<PreviewCacheCoordination>();
        var coordination = new PreviewCacheCoordination(logger);
        using IDisposable exclusiveLease = await coordination.AcquireExclusiveAsync(CancellationToken.None);
        Task<int> request = coordination.ExecuteEntryAsync(
            EntryPath,
            static _ => Task.FromResult(1),
            CancellationToken.None);
        Assert.False(request.IsCompleted);

        exclusiveLease.Dispose();
        Assert.Equal(1, await request.WaitAsync(coordinationTimeout));

        Assert.NotEmpty(logger.Events);
        Assert.All(
            logger.Events,
            recorded => Assert.Equal(["{OriginalFormat}"], recorded.Properties.Keys.ToArray()));
    }

    [Fact]
    public async Task ReportsNoCoordinationEventsWhenTheHostDisablesDebugLogging()
    {
        var logger = new DebugProtocolLogger<PreviewCacheCoordination>(LogLevel.Information);
        var coordination = new PreviewCacheCoordination(logger);
        using IDisposable exclusiveLease = await coordination.AcquireExclusiveAsync(CancellationToken.None);
        Task<int> request = coordination.ExecuteEntryAsync(
            EntryPath,
            static _ => Task.FromResult(1),
            CancellationToken.None);
        Assert.False(request.IsCompleted);

        exclusiveLease.Dispose();
        Assert.Equal(1, await request.WaitAsync(coordinationTimeout));

        Assert.Empty(logger.Events);
    }
}
