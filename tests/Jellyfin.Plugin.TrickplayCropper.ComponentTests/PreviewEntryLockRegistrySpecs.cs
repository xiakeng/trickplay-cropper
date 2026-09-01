using Jellyfin.Plugin.TrickplayCropper.Caching;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class PreviewEntryLockRegistrySpecs
{
    private static readonly TimeSpan coordinationTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RemovesEntryAfterLastParticipantLeaves()
    {
        var registry = new PreviewEntryLockRegistry(StringComparer.Ordinal);
        IDisposable owner = await registry.AcquireAsync("/cache/entry.jpg", CancellationToken.None);
        Task<IDisposable> waiterTask = registry
            .AcquireAsync("/cache/entry.jpg", CancellationToken.None)
            .AsTask();

        Assert.Equal(1, registry.EntryCount);
        Assert.False(waiterTask.IsCompleted);

        owner.Dispose();
        IDisposable waiter = await waiterTask.WaitAsync(coordinationTimeout);
        Assert.Equal(1, registry.EntryCount);

        waiter.Dispose();
        Assert.Equal(0, registry.EntryCount);
    }

    [Fact]
    public async Task CancelledWaiterDoesNotLeakEntryReference()
    {
        var registry = new PreviewEntryLockRegistry(StringComparer.Ordinal);
        IDisposable owner = await registry.AcquireAsync("/cache/entry.jpg", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        Task<IDisposable> waiterTask = registry
            .AcquireAsync("/cache/entry.jpg", cancellation.Token)
            .AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waiterTask.WaitAsync(coordinationTimeout));
        Assert.Equal(1, registry.EntryCount);

        owner.Dispose();
        Assert.Equal(0, registry.EntryCount);
    }

    [Fact]
    public async Task CaseInsensitiveIdentityCoordinatesEquivalentPaths()
    {
        var registry = new PreviewEntryLockRegistry(StringComparer.OrdinalIgnoreCase);
        IDisposable owner = await registry.AcquireAsync("/cache/Entry.jpg", CancellationToken.None);
        Task<IDisposable> waiterTask = registry
            .AcquireAsync("/cache/entry.jpg", CancellationToken.None)
            .AsTask();

        Assert.False(waiterTask.IsCompleted);

        owner.Dispose();
        using IDisposable waiter = await waiterTask.WaitAsync(coordinationTimeout);
    }

    [Fact]
    public async Task OrdinalIdentityKeepsDifferentlyCasedPathsIndependent()
    {
        var registry = new PreviewEntryLockRegistry(StringComparer.Ordinal);
        using IDisposable first = await registry.AcquireAsync("/cache/Entry.jpg", CancellationToken.None);
        using IDisposable second = await registry.AcquireAsync("/cache/entry.jpg", CancellationToken.None);

        Assert.Equal(2, registry.EntryCount);
    }
}
