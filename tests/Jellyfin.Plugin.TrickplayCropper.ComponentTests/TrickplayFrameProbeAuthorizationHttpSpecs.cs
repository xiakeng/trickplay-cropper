using System.Net;
using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayFrameProbeAuthorizationHttpSpecs
{
    [Fact]
    public async Task ComposesGetWithDefaultAuthorizationAndProbeWithOnlyItsNamedPolicy()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        AuthorizationPolicy getPolicy = await ResolvePolicyAsync(fixture, "GET");
        AuthorizationPolicy probePolicy = await ResolvePolicyAsync(fixture, "HEAD");

        Assert.Contains(getPolicy.Requirements, requirement => requirement is TestDefaultAuthorizationRequirement);
        Assert.Equal([TestAuthenticationHandler.SchemeName], probePolicy.AuthenticationSchemes);
        Assert.Contains(probePolicy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(
            probePolicy.Requirements,
            requirement => requirement is TrickplayFrameProbeIdentityRequirement);
        Assert.DoesNotContain(
            probePolicy.Requirements,
            requirement => requirement is TestDefaultAuthorizationRequirement);
    }

    [Fact]
    public async Task ReusesNativeAuthenticationWithoutDefaultAuthorizationUserLoadForProbe()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, scenario.NativeAuthenticationUserLoads);
        Assert.Equal(0, scenario.DefaultAuthorizationUserLoads);
        Assert.Equal(0, scenario.UserLookups);
    }

    [Fact]
    public async Task RetainsDefaultAuthorizationAndCurrentUserLoadsForGet()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, scenario.NativeAuthenticationUserLoads);
        Assert.Equal(1, scenario.DefaultAuthorizationUserLoads);
        Assert.Equal(1, scenario.UserLookups);
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.Forbidden)]
    [InlineData("", "false", HttpStatusCode.Forbidden)]
    [InlineData("not-a-guid", "false", HttpStatusCode.Forbidden)]
    [InlineData("00000000000000000000000000000000", "false", HttpStatusCode.Forbidden)]
    [InlineData("", "not-a-boolean", HttpStatusCode.Forbidden)]
    [InlineData("", "true", HttpStatusCode.OK)]
    [InlineData("11111111111111111111111111111111", "not-a-boolean", HttpStatusCode.OK)]
    public async Task RequiresAValidNativeUserOrBooleanTrueApiKey(
        string? userIdClaim,
        string? isApiKeyClaim,
        HttpStatusCode expectedStatus)
    {
        var scenario = new PreviewScenario
        {
            NativeClaims = CreateClaims(userIdClaim, isApiKeyClaim),
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ChallengesAnUnrelatedAuthenticatedIdentity()
    {
        var scenario = new PreviewScenario { Authentication = AuthenticationState.UnrelatedIdentity };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
    }

    private static Claim[] CreateClaims(string? userIdClaim, string? isApiKeyClaim)
    {
        List<Claim> claims = [];
        if (userIdClaim is not null)
        {
            claims.Add(new Claim("Jellyfin-UserId", userIdClaim));
        }

        if (isApiKeyClaim is not null)
        {
            claims.Add(new Claim("Jellyfin-IsApiKey", isApiKeyClaim));
        }

        return [.. claims];
    }

    private static async Task<AuthorizationPolicy> ResolvePolicyAsync(PreviewHostFixture fixture, string method)
    {
        EndpointDataSource source = fixture.Services.GetRequiredService<EndpointDataSource>();
        Endpoint endpoint = Assert.Single(source.Endpoints, candidate => IsAction(candidate, method));
        IAuthorizeData[] authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ToArray();
        IAuthorizationPolicyProvider provider = fixture.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        AuthorizationPolicy? policy = await AuthorizationPolicy.CombineAsync(provider, authorizeData);
        return Assert.IsType<AuthorizationPolicy>(policy);
    }

    private static bool IsAction(Endpoint endpoint, string method)
    {
        ControllerActionDescriptor? action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
        HttpMethodMetadata? methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        return action?.ControllerTypeInfo.AsType() == typeof(TrickplayPreviewController)
            && methods?.HttpMethods.Contains(method, StringComparer.Ordinal) == true;
    }
}
