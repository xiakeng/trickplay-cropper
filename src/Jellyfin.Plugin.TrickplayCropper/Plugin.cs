using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.TrickplayCropper;

public sealed class Plugin : BasePlugin
{
    public Plugin()
    {
        // Unlike BasePlugin<T>, BasePlugin leaves the attributes required by
        // Jellyfin's PluginManager unset. This plugin has no configuration store.
        System.Reflection.Assembly assembly = typeof(Plugin).Assembly;
        SetAttributes(assembly.Location, Path.GetDirectoryName(assembly.Location)!, assembly.GetName().Version!);
    }

    public override string Name => PluginIdentity.Name;

    public override Guid Id => PluginIdentity.Id;
}
