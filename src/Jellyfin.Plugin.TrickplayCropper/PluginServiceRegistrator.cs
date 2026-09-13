using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Authorization;
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
        serviceCollection.Configure<AuthorizationOptions>(ConfigureAuthorization);
        serviceCollection.TryAddSingleton(TimeProvider.System);
        RegisterPreviewServices(serviceCollection);
    }

    private static void RegisterPreviewServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<ITrickplayFrameTimeline, TrickplayFrameTimeline>();
        serviceCollection.AddSingleton<ITrickplayPreview, TrickplayPreview>();
        serviceCollection.AddSingleton<ITrickplayFrameProbe, TrickplayFrameProbe>();
        serviceCollection.AddSingleton<JellyfinPreviewContextResolver>();
        serviceCollection.AddSingleton<IFrameTimelineContextResolver>(
            static services => services.GetRequiredService<JellyfinPreviewContextResolver>());
        serviceCollection.AddSingleton<IPreviewContextResolver>(
            static services => services.GetRequiredService<JellyfinPreviewContextResolver>());
        serviceCollection.AddSingleton<TrickplaySourceFactsCache>();
        serviceCollection.AddSingleton<ITrickplayFrameProbeContextResolver, JellyfinTrickplayFrameProbeContextResolver>();
        serviceCollection.AddSingleton<TrickplayMetadataCache>();
        serviceCollection.AddSingleton<ITrickplayFrameCalculationResolver, JellyfinTrickplayFrameCalculationResolver>();
        serviceCollection.AddSingleton<IPreviewSourceResolver, JellyfinPreviewSourceResolver>();
        serviceCollection.AddSingleton<DiskPreviewCache>();
        serviceCollection.AddSingleton<IPreviewCache>(
            static services => services.GetRequiredService<DiskPreviewCache>());
        serviceCollection.AddSingleton<IPreviewCacheMaintenance>(
            static services => services.GetRequiredService<DiskPreviewCache>());
        serviceCollection.AddSingleton<ITrickplayPreviewEncoder, TrickplayPreviewEncoder>();
    }

    private static void ConfigureAuthorization(AuthorizationOptions options)
    {
        options.AddPolicy(
            nameof(TrickplayFrameProbe),
            policy => policy
                .AddAuthenticationSchemes("CustomAuthentication")
                .RequireAuthenticatedUser()
                .RequireAssertion(HasNativeIdentity));
    }

    private static bool HasNativeIdentity(AuthorizationHandlerContext context)
    {
        Claim? apiKeyClaim = context.User.FindFirst(
            claim => claim.Type.Equals(
                JellyfinPreviewContextResolver.JellyfinIsApiKeyClaim,
                StringComparison.OrdinalIgnoreCase));
        if (bool.TryParse(apiKeyClaim?.Value, out bool isApiKey) && isApiKey)
        {
            return true;
        }

        Claim? userIdClaim = context.User.FindFirst(
            claim => claim.Type.Equals(
                JellyfinPreviewContextResolver.JellyfinUserIdClaim,
                StringComparison.OrdinalIgnoreCase));
        return Guid.TryParse(userIdClaim?.Value, out Guid userId) && userId != Guid.Empty;
    }
}
