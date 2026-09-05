using System.Diagnostics;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Runs an explicit executable without a shell or credential-bearing arguments.</summary>
internal sealed class HarnessProcess
{
    /// <summary>Runs an operator-visible process, preserving the terminal for interactive sudo.</summary>
    public static async Task<int> RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo start = new(executable) { UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new IOException("Could not start a required host operation.");
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }
}
