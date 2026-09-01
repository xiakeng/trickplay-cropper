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
    public void JellyfinDiscoversRequiredHostTypes()
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
    public void PreviewEndpointUsesTheApprovedBindingContract()
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
        ParameterInfo cancellationToken = Assert.Single(
            action.GetParameters(),
            parameter => parameter.Name == "cancellationToken");
        Assert.Equal(typeof(Guid), itemId.ParameterType);
        Assert.Equal(typeof(PreviewQueryParameters), parameters.ParameterType);
        Assert.Equal(typeof(CancellationToken), cancellationToken.ParameterType);
        Assert.NotNull(itemId.GetCustomAttribute<FromRouteAttribute>());
        Assert.NotNull(parameters.GetCustomAttribute<FromQueryAttribute>());

        PropertyInfo[] queryProperties = typeof(PreviewQueryParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["MediaSourceId", "PositionTicks"], queryProperties.Select(property => property.Name));
        Assert.Equal(typeof(Guid?), queryProperties[0].PropertyType);
        Assert.Equal(typeof(long), queryProperties[1].PropertyType);
        PropertyInfo positionTicks = Assert.IsAssignableFrom<PropertyInfo>(
            typeof(PreviewQueryParameters).GetProperty(nameof(PreviewQueryParameters.PositionTicks)));
        Assert.NotNull(positionTicks.GetCustomAttribute<BindRequiredAttribute>());
    }

    [Fact]
    public void ExportedSurfaceContainsOnlyRequiredContracts()
    {
        string[] exportedTypes = typeof(Plugin).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Jellyfin.Plugin.TrickplayCropper.Api.PreviewQueryParameters",
                "Jellyfin.Plugin.TrickplayCropper.Api.TrickplayPreviewController",
                "Jellyfin.Plugin.TrickplayCropper.Caching.IPreviewCacheMaintenance",
                "Jellyfin.Plugin.TrickplayCropper.Caching.PreviewCacheDisposition",
                "Jellyfin.Plugin.TrickplayCropper.Plugin",
                "Jellyfin.Plugin.TrickplayCropper.PluginServiceRegistrator",
                "Jellyfin.Plugin.TrickplayCropper.Preview.ITrickplayPreview",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewOutcome",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewOutcome+BadRequest",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewOutcome+Forbidden",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewOutcome+InternalError",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewOutcome+NotFound",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewOutcome+NotModified",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewOutcome+Ok",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewOutcome+Unauthorized",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewQuery",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewTelemetry",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewTelemetry+CacheAccess",
                "Jellyfin.Plugin.TrickplayCropper.Preview.PreviewTelemetry+Conditional",
                "Jellyfin.Plugin.TrickplayCropper.Tasks.ClearTrickplayCropperCacheTask",
            ],
            exportedTypes);
    }
}
