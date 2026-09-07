using System.Net;
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
    [Fact]
    public async Task KeepsDefaultAuthorizationOnGetButNotOnTheTrickplayFrameProbe()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        AuthorizationPolicy getPolicy = await GetEffectivePolicyAsync(fixture, nameof(Api.TrickplayPreviewController.GetAsync));
        AuthorizationPolicy probePolicy = await GetEffectivePolicyAsync(fixture, nameof(Api.TrickplayPreviewController.HeadAsync));

        Assert.Contains(getPolicy.Requirements, requirement => requirement is TestDefaultAuthorizationRequirement);
        Assert.DoesNotContain(probePolicy.Requirements, requirement => requirement is TestDefaultAuthorizationRequirement);
        Assert.Equal(["CustomAuthentication"], probePolicy.AuthenticationSchemes);
    }

    [Theory]
    [InlineData(AuthenticationState.FalseApiKeyWithMissingUserId)]
    [InlineData(AuthenticationState.MalformedApiKeyWithoutCurrentUser)]
    [InlineData(AuthenticationState.EmptyUserId)]
    [InlineData(AuthenticationState.MalformedUserId)]
    [InlineData(AuthenticationState.UnrelatedIdentity)]
    public async Task ForbidsAuthenticatedIdentitiesWithoutValidNativeClaims(AuthenticationState authentication)
    {
        var scenario = new PreviewScenario { Authentication = authentication };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertBodylessTrickplayFrameProbeFailureAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AcceptsAValidUserIdWhenTheApiKeyClaimIsMalformed()
    {
        var scenario = new PreviewScenario { Authentication = AuthenticationState.MalformedApiKeyWithValidUserId };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertTrickplayFrameProbeSuccessAsync(response, 0);
    }

    private static async Task<AuthorizationPolicy> GetEffectivePolicyAsync(
        PreviewHostFixture fixture,
        string actionName)
    {
        EndpointDataSource endpoints = fixture.Services.GetRequiredService<EndpointDataSource>();
        Endpoint endpoint = endpoints.Endpoints.Single(candidate =>
            candidate.Metadata.GetMetadata<ControllerActionDescriptor>()?.MethodInfo.Name == actionName);
        IAuthorizationPolicyProvider provider = fixture.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        IEnumerable<IAuthorizeData> metadata = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        AuthorizationPolicy? policy = await AuthorizationPolicy.CombineAsync(provider, metadata);
        return Assert.IsType<AuthorizationPolicy>(policy);
    }
}
