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
    private const string FrameProbePolicyName = "TrickplayFrameProbe";
    private const string JellyfinAuthenticationScheme = "CustomAuthentication";
    private const string JellyfinIsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string JellyfinUserIdClaim = "Jellyfin-UserId";

    /// <summary>
    /// Registers all process-wide Trickplay Cropper modules as singletons.
    /// </summary>
    /// <param name="serviceCollection">The Jellyfin service collection.</param>
    /// <param name="applicationHost">The current Jellyfin application host.</param>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(applicationHost);
        serviceCollection.Configure<AuthorizationOptions>(ConfigureFrameProbeAuthorization);
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<ITrickplayPreview, TrickplayPreview>();
        serviceCollection.AddSingleton<ITrickplayFrameProbe, TrickplayFrameProbe>();
        serviceCollection.AddSingleton<IPreviewContextResolver, JellyfinPreviewContextResolver>();
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

    private static void ConfigureFrameProbeAuthorization(AuthorizationOptions options)
    {
        options.AddPolicy(FrameProbePolicyName, policy =>
        {
            policy.AddAuthenticationSchemes(JellyfinAuthenticationScheme);
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(HasNativeIdentity);
        });
    }

    private static bool HasNativeIdentity(AuthorizationHandlerContext context)
    {
        string? apiKeyValue = context.User.FindFirst(JellyfinIsApiKeyClaim)?.Value;
        if (bool.TryParse(apiKeyValue, out bool isApiKey) && isApiKey)
        {
            return true;
        }

        string? userIdValue = context.User.FindFirst(JellyfinUserIdClaim)?.Value;
        return Guid.TryParse(userIdValue, out Guid userId) && userId != Guid.Empty;
    }
}
