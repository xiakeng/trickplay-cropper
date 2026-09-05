using System.Diagnostics;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class HarnessHostOperationSpecs
{
    [Fact]
    public async Task PreservesTheFilesystemBoundaryAndRestoresLoggingOnFailure()
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
        start.ArgumentList.Add(Path.Combine(root.FullName, "tools/TrickplayCropper.IntegrationHarness/host_operation_specs.py"));
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
