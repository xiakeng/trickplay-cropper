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
    private const string IsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string NativeAuthenticationScheme = "CustomAuthentication";
    private const string UserIdClaim = "Jellyfin-UserId";

    internal const string FrameProbePolicyName = "TrickplayFrameProbe";

    /// <summary>
    /// Registers all process-wide Trickplay Cropper modules as singletons.
    /// </summary>
    /// <param name="serviceCollection">The Jellyfin service collection.</param>
    /// <param name="applicationHost">The current Jellyfin application host.</param>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(applicationHost);
        serviceCollection.Configure<AuthorizationOptions>(options =>
            options.AddPolicy(FrameProbePolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(NativeAuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasNativeIdentity(context.User));
            }));
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

    private static bool HasNativeIdentity(ClaimsPrincipal principal)
    {
        string? apiKey = principal.FindFirst(IsApiKeyClaim)?.Value;
        if (bool.TryParse(apiKey, out bool isApiKey) && isApiKey)
        {
            return true;
        }

        string? userId = principal.FindFirst(UserIdClaim)?.Value;
        return Guid.TryParse(userId, out Guid parsedUserId) && parsedUserId != Guid.Empty;
    }
}
