using MediaBrowser.Common.Plugins;

namespace Jellyfin.Plugin.TrickplayCropper;

public sealed class Plugin : BasePlugin
{
    public override string Name => PluginIdentity.Name;

    public override Guid Id => PluginIdentity.Id;
}
