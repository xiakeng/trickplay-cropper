using System.Diagnostics;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class HarnessHostOperationSpecs
{
    [Theory]
    [InlineData("host_operation_specs.py")]
    [InlineData("request_cost_specs.py")]
    public async Task PassesTheIsolatedPythonHarnessContracts(string script)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TrickplayCropper.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        ProcessStartInfo start = new("/usr/bin/python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add(Path.Combine(root.FullName, "tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests", script));
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
