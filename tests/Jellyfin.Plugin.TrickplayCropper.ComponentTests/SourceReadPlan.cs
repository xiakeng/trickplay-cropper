namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed class SourceReadPlan
{
    private readonly TaskCompletionSource release = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource started = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public SourceReadPlan(SourceMembership membership, int? sourceVideoWidth)
    {
        Membership = membership;
        SourceVideoWidth = sourceVideoWidth;
    }

    public SourceMembership Membership { get; }

    public int? SourceVideoWidth { get; }

    public bool SwallowsCancellation { get; set; }

    public Task Started => started.Task;

    public void Release()
    {
        release.TrySetResult();
    }

    public async Task WaitForReleaseAsync(CancellationToken cancellationToken)
    {
        started.TrySetResult();
        try
        {
            await release.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (SwallowsCancellation && cancellationToken.IsCancellationRequested)
        {
            // Jellyfin's provider boundary omits failed providers, including canceled ones.
        }
    }
}
