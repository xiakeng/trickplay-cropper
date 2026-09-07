using Jellyfin.Plugin.TrickplayCropper.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayPreviewAuthorizationPolicySpecs
{
    [Fact]
    public async Task SeparatesEffectiveGetAndProbeAuthorizationPolicies()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        AuthorizationPolicy getPolicy = await ResolvePolicyAsync(fixture, nameof(TrickplayPreviewController.GetAsync));
        AuthorizationPolicy probePolicy = await ResolvePolicyAsync(
            fixture,
            nameof(TrickplayPreviewController.HeadAsync));

        Assert.Contains(getPolicy.Requirements, requirement => requirement is TestDefaultAuthorizationRequirement);
        Assert.DoesNotContain(probePolicy.Requirements, requirement => requirement is TestDefaultAuthorizationRequirement);
        Assert.Equal(["CustomAuthentication"], probePolicy.AuthenticationSchemes);
    }

    private static async Task<AuthorizationPolicy> ResolvePolicyAsync(
        PreviewHostFixture fixture,
        string actionName)
    {
        EndpointDataSource source = fixture.Services.GetRequiredService<EndpointDataSource>();
        Endpoint endpoint = Assert.Single(
            source.Endpoints,
            endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.MethodInfo.Name == actionName);
        IReadOnlyList<IAuthorizeData> authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        IAuthorizationPolicyProvider provider = fixture.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        AuthorizationPolicy? policy = await AuthorizationPolicy.CombineAsync(provider, authorizeData);
        return Assert.IsType<AuthorizationPolicy>(policy);
    }
}
