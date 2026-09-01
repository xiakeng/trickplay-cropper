using System.Reflection;
using Jellyfin.Plugin.TrickplayCropper.Api;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Jellyfin.Plugin.TrickplayCropper.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class PluginDiscoverySpecs
{
    [Fact]
    public void JellyfinDiscoversThePluginControllerTaskAndServiceRegistrator()
    {
        Assert.True(typeof(Plugin).IsPublic);
        Assert.True(typeof(TrickplayPreviewController).IsPublic);
        Assert.True(typeof(ClearTrickplayCropperCacheTask).IsPublic);
        Assert.True(typeof(PluginServiceRegistrator).IsPublic);
        Assert.True(typeof(BasePlugin).IsAssignableFrom(typeof(Plugin)));
        Assert.True(typeof(IScheduledTask).IsAssignableFrom(typeof(ClearTrickplayCropperCacheTask)));
        Assert.True(typeof(IPluginServiceRegistrator).IsAssignableFrom(typeof(PluginServiceRegistrator)));
    }

    [Fact]
    public void PreviewEndpointUsesTheApprovedRouteAndBindingContract()
    {
        Type controller = typeof(TrickplayPreviewController);
        Assert.NotNull(controller.GetCustomAttribute<ApiControllerAttribute>());
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        RouteAttribute route = Assert.IsType<RouteAttribute>(controller.GetCustomAttribute<RouteAttribute>());
        Assert.Equal("TrickplayCropper/Videos/{itemId}/Preview", route.Template);

        MethodInfo action = Assert.IsAssignableFrom<MethodInfo>(
            controller.GetMethod(nameof(TrickplayPreviewController.GetAsync)));
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        ParameterInfo itemId = Assert.Single(action.GetParameters(), parameter => parameter.Name == "itemId");
        ParameterInfo parameters = Assert.Single(action.GetParameters(), parameter => parameter.Name == "parameters");
        Assert.NotNull(itemId.GetCustomAttribute<FromRouteAttribute>());
        Assert.NotNull(parameters.GetCustomAttribute<FromQueryAttribute>());

        PropertyInfo positionTicks = Assert.IsAssignableFrom<PropertyInfo>(
            typeof(PreviewQueryParameters).GetProperty(nameof(PreviewQueryParameters.PositionTicks)));
        Assert.NotNull(positionTicks.GetCustomAttribute<BindRequiredAttribute>());
    }

    [Fact]
    public void ProductionImplementationsRemainInternalByDefault()
    {
        Type[] implementations =
        [
            typeof(DiskPreviewCache),
            typeof(FrameSelection),
            typeof(JellyfinPreviewSourceResolver),
            typeof(PreviewIdentity),
            typeof(TrickplayPreview),
            typeof(TrickplayPreviewEncoder),
        ];

        Assert.All(implementations, implementation => Assert.True(implementation.IsNotPublic));
    }
}
