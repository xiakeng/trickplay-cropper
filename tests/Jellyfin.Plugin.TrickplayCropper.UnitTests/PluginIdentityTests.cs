namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

using Xunit;

public sealed class PluginIdentityTests
{
    [Fact]
    public void IdentityUsesTheInstallContract()
    {
        var plugin = new Plugin();

        Assert.Equal("Trickplay Cropper", plugin.Name);
        Assert.Equal(Guid.Parse("630fb758-9a29-4f2c-a54c-95793651bb8a"), plugin.Id);
    }

    [Fact]
    public void AssemblyUsesTheInitialPluginVersion()
    {
        Assert.Equal(new Version(1, 0, 0, 0), typeof(Plugin).Assembly.GetName().Version);
    }
}
