namespace Jellyfin.Plugin.TrickplayCropper;

using MediaBrowser.Common.Plugins;

public sealed class Plugin : BasePlugin
{
    public override string Name => PluginIdentity.Name;

    public override Guid Id => PluginIdentity.Id;
}
