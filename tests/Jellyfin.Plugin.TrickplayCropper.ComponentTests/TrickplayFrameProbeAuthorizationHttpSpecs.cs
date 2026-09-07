using System.Net;
using Jellyfin.Plugin.TrickplayCropper.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.TrickplayPreviewHttpSupport;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayFrameProbeAuthorizationHttpSpecs
{
    [Theory]
    [InlineData(AuthenticationState.UserSessionWithoutUserIdClaim)]
    [InlineData(AuthenticationState.UserSessionWithEmptyUserIdClaim)]
    [InlineData(AuthenticationState.UserSessionWithMalformedUserIdClaim)]
    [InlineData(AuthenticationState.SessionWithoutUserAndFalseApiKeyClaim)]
    [InlineData(AuthenticationState.SessionWithoutUserAndMalformedApiKeyClaim)]
    [InlineData(AuthenticationState.UnrelatedIdentity)]
    public async Task ForbidsAuthenticatedIdentitiesWithoutValidNativeClaims(AuthenticationState authentication)
    {
        var scenario = new PreviewScenario { Authentication = authentication };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertBodylessTrickplayFrameProbeFailureAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task KeepsTheDefaultAuthorizationRequirementOnlyOnGet()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        AuthorizationPolicy getPolicy = await GetEffectivePolicyAsync(
            fixture,
            nameof(TrickplayPreviewController.GetAsync));
        AuthorizationPolicy headPolicy = await GetEffectivePolicyAsync(
            fixture,
            nameof(TrickplayPreviewController.HeadAsync));

        Assert.Contains(getPolicy.Requirements, requirement => requirement is TestDefaultAuthorizationRequirement);
        Assert.DoesNotContain(headPolicy.Requirements, requirement => requirement is TestDefaultAuthorizationRequirement);
        Assert.Equal(["CustomAuthentication"], headPolicy.AuthenticationSchemes);
    }

    private static async Task<AuthorizationPolicy> GetEffectivePolicyAsync(
        PreviewHostFixture fixture,
        string actionName)
    {
        EndpointDataSource endpoints = fixture.Services.GetRequiredService<EndpointDataSource>();
        Endpoint endpoint = Assert.Single(
            endpoints.Endpoints,
            candidate => candidate.Metadata.GetMetadata<ControllerActionDescriptor>()?.MethodInfo.Name == actionName);
        IAuthorizationPolicyProvider provider = fixture.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        AuthorizationPolicy? policy = await AuthorizationPolicy.CombineAsync(
            provider,
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        return Assert.IsType<AuthorizationPolicy>(policy);
    }
}
