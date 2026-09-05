using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.TrickplayCropper;

/// <summary>
/// Registers Trickplay Cropper production modules with the Jellyfin host.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <summary>
    /// Registers all process-wide Trickplay Cropper modules as singletons.
    /// </summary>
    /// <param name="serviceCollection">The Jellyfin service collection.</param>
    /// <param name="applicationHost">The current Jellyfin application host.</param>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(applicationHost);
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<ITrickplayPreview, TrickplayPreview>();
        serviceCollection.AddSingleton<ITrickplayFrameProbe, TrickplayFrameProbe>();
        serviceCollection.AddSingleton<IPreviewContextResolver, JellyfinPreviewContextResolver>();
        serviceCollection.AddSingleton<ITrickplayFrameProbeContextResolver, JellyfinTrickplayFrameProbeContextResolver>();
        serviceCollection.AddSingleton<ITrickplayFrameCalculationResolver, JellyfinTrickplayFrameCalculationResolver>();
        serviceCollection.AddSingleton<IPreviewSourceResolver, JellyfinPreviewSourceResolver>();
        serviceCollection.AddSingleton<DiskPreviewCache>();
        serviceCollection.AddSingleton<IPreviewCache>(
            static services => services.GetRequiredService<DiskPreviewCache>());
        serviceCollection.AddSingleton<IPreviewCacheMaintenance>(
            static services => services.GetRequiredService<DiskPreviewCache>());
        serviceCollection.AddSingleton<ITrickplayPreviewEncoder, TrickplayPreviewEncoder>();
    }
}
