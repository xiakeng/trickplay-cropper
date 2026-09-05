using TrickplayCropper.IntegrationHarness;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class DeploymentCycleSpecs
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoresAfterBothSuccessfulAndFailedVerification(bool failVerification)
    {
        using StringWriter output = new();
        DeploymentCycle cycle = new(output);
        int boundaries = 0;
        bool restored = false;
        bool result = await cycle.RunAsync(
            () => { boundaries++; return Task.FromResult(0); },
            () => failVerification ? Task.FromException(new InvalidOperationException("private diagnostic")) : Task.CompletedTask,
            () => { boundaries++; restored = true; return Task.CompletedTask; });
        Assert.True(restored);
        Assert.Equal(2, boundaries);
        Assert.Equal(!failVerification, result);
        Assert.DoesNotContain("private diagnostic", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    public async Task DoesNotRestoreAnotherRunsSnapshotOrAnIncompleteSnapshot(int deploymentStatus)
    {
        using StringWriter output = new();
        bool verified = false;
        bool restored = false;
        bool result = await new DeploymentCycle(output).RunAsync(
            () => Task.FromResult(deploymentStatus),
            () => { verified = true; return Task.CompletedTask; },
            () => { restored = true; return Task.CompletedTask; });
        Assert.False(result);
        Assert.False(verified);
        Assert.False(restored);
    }

    [Fact]
    public async Task RestoresAfterAPartialDeploymentWithoutRunningVerification()
    {
        using StringWriter output = new();
        bool restored = false;
        bool result = await new DeploymentCycle(output).RunAsync(
            () => Task.FromResult(22),
            () => throw new Xunit.Sdk.XunitException("Verification must not run after failed deployment."),
            () => { restored = true; return Task.CompletedTask; });
        Assert.False(result);
        Assert.True(restored);
    }

    [Fact]
    public async Task ReportsRestorationFailureEvenWhenVerificationSucceeds()
    {
        using StringWriter output = new();
        bool result = await new DeploymentCycle(output).RunAsync(
            () => Task.FromResult(0), () => Task.CompletedTask,
            () => Task.FromException(new IOException("secret")));
        Assert.False(result);
        Assert.Contains("RESTORATION FAILED", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationStillRunsRestoration()
    {
        using StringWriter output = new();
        bool restored = false;
        bool result = await new DeploymentCycle(output).RunAsync(
            () => Task.FromResult(0),
            () => Task.FromCanceled(new CancellationToken(true)),
            () => { restored = true; return Task.CompletedTask; });
        Assert.False(result);
        Assert.True(restored);
    }
}
