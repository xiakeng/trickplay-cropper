namespace TrickplayCropper.IntegrationHarness;

/// <summary>Releases one request wave only after all six lanes have arrived.</summary>
internal sealed class StormBarrier
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int remaining = ScrubStormPlan.Clients * ScrubStormPlan.LanesPerClient;

    /// <summary>Waits asynchronously and lets a failed lane cancel its peers.</summary>
    public Task ArriveAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref remaining) == 0)
        {
            release.SetResult();
        }

        return release.Task.WaitAsync(cancellationToken);
    }
}
