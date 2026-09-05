using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class PluginHostActivationSpecs
{
    [Fact]
    public void ConstructedPluginProvidesTheAssemblyAttributesRequiredByJellyfin()
    {
        var plugin = new Plugin();

        Assert.Equal(typeof(Plugin).Assembly.GetName().Version, plugin.Version);
        Assert.Equal(typeof(Plugin).Assembly.Location, plugin.AssemblyFilePath);
        Assert.Equal(Path.GetDirectoryName(plugin.AssemblyFilePath), plugin.DataFolderPath);
        Assert.Equal(plugin.Version.ToString(), plugin.GetPluginInfo().Version.ToString());
    }
}
