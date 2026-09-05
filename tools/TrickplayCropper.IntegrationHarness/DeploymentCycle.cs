namespace TrickplayCropper.IntegrationHarness;

/// <summary>Restores the host after every verification outcome once this run owns its Logging Snapshot.</summary>
public sealed class DeploymentCycle(TextWriter output)
{
    /// <summary>Crosses the two privilege boundaries and retains both verification and restoration failures.</summary>
    public async Task<bool> RunAsync(Func<Task<int>> deploy, Func<Task> verify, Func<Task> restore)
    {
        ArgumentNullException.ThrowIfNull(deploy);
        ArgumentNullException.ThrowIfNull(verify);
        ArgumentNullException.ThrowIfNull(restore);
        int status = await deploy().ConfigureAwait(false);
        if (status is not (0 or 22))
        {
            output.WriteLine(status == 20
                ? "A surviving Logging Snapshot blocks this run; human inspection is required."
                : "Deployment did not establish snapshot ownership; the cycle did not start.");
            return false;
        }

        bool verified = false;
        bool restored = false;
        try
        {
            if (status == 0)
            {
                await verify().ConfigureAwait(false);
                verified = true;
            }
        }
        catch (Exception)
        {
            // This is the cycle's shutdown boundary. Never print raw HTTP or parser exceptions.
            output.WriteLine("Verification failed or was cancelled; restoring logging and service health.");
        }
        finally
        {
            try
            {
                await restore().ConfigureAwait(false);
                restored = true;
                output.WriteLine("Logging restored byte-for-byte; restoration restart is healthy.");
            }
            catch (Exception)
            {
                output.WriteLine("RESTORATION FAILED. Inspect logging.json and its snapshot before another run.");
            }
        }

        return verified && restored;
    }
}
