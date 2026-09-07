using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.TrickplayCropper.Api;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.PreviewHttpTestValues;
using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.TrickplayPreviewHttpSupport;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayPreviewKestrelHttpSpecs
{
    [Theory]
    [InlineData(TrickplayFrameProbeKestrelCondition.Success, HttpStatusCode.OK)]
    [InlineData(TrickplayFrameProbeKestrelCondition.MalformedInput, HttpStatusCode.BadRequest)]
    [InlineData(TrickplayFrameProbeKestrelCondition.UnauthenticatedSession, HttpStatusCode.Unauthorized)]
    [InlineData(TrickplayFrameProbeKestrelCondition.DefaultPolicyDenied, HttpStatusCode.OK)]
    [InlineData(TrickplayFrameProbeKestrelCondition.ApiKeyWithoutCurrentUser, HttpStatusCode.OK)]
    [InlineData(TrickplayFrameProbeKestrelCondition.ConcealedResource, HttpStatusCode.NotFound)]
    [InlineData(TrickplayFrameProbeKestrelCondition.InvalidMetadata, HttpStatusCode.InternalServerError)]
    public async Task ProvesBodylessTrickplayFrameProbeOutcomesOverRealKestrel(
        TrickplayFrameProbeKestrelCondition condition,
        HttpStatusCode expectedStatus)
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(
            CreateKestrelScenario(condition));

        using HttpResponseMessage response = condition == TrickplayFrameProbeKestrelCondition.MalformedInput
            ? await fixture.HeadRawAsync(PreviewPath)
            : await fixture.HeadAsync();

        if (expectedStatus == HttpStatusCode.OK)
        {
            await AssertTrickplayFrameProbeSuccessAsync(response, 0);
        }
        else
        {
            await AssertBodylessTrickplayFrameProbeFailureAsync(response, expectedStatus);
        }
    }

    [Fact]
    public async Task ProvesApiKeyProbeAndPreviewAuthorizationSplitOverRealKestrel()
    {
        var scenario = new PreviewScenario
        {
            Authentication = AuthenticationState.ApiKeyWithoutCurrentUser,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);

        using HttpResponseMessage probeResponse = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(probeResponse, 0);

        using HttpResponseMessage previewResponse = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.Forbidden, previewResponse.StatusCode);
        await AssertAuthorizationErrorResponseAsync(previewResponse);
    }

    [Theory]
    [InlineData(AuthenticationState.UserSessionWithoutUserId)]
    [InlineData(AuthenticationState.UserSessionWithEmptyUserId)]
    [InlineData(AuthenticationState.UserSessionWithMalformedUserId)]
    [InlineData(AuthenticationState.UserSessionWithMalformedApiKeyClaim)]
    [InlineData(AuthenticationState.UnrelatedIdentity)]
    public async Task ForbidsAuthenticatedIdentitiesWithoutNativeUserOrApiKeyClaims(
        AuthenticationState authentication)
    {
        var scenario = new PreviewScenario { Authentication = authentication };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertBodylessTrickplayFrameProbeFailureAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ComposesSeparateProbeAndPreviewAuthorizationPolicies()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(
            new PreviewScenario());

        AuthorizationPolicy probePolicy = await GetPolicyAsync(fixture, nameof(TrickplayPreviewController.HeadAsync));
        AuthorizationPolicy previewPolicy = await GetPolicyAsync(fixture, nameof(TrickplayPreviewController.GetAsync));

        Assert.Equal([TestAuthenticationHandler.SchemeName], probePolicy.AuthenticationSchemes);
        Assert.DoesNotContain(probePolicy.Requirements, requirement => requirement is TestDefaultAuthorizationRequirement);
        Assert.Contains(previewPolicy.Requirements, requirement => requirement is TestDefaultAuthorizationRequirement);
    }

    [Fact]
    public async Task ReturnsTheSelectedGetFrameIndexOverRealKestrelForImageAndConditionalSuccess()
    {
        var scenario = new PreviewScenario
        {
            RequestPositionTicks = 30_000L * TimeSpan.TicksPerMillisecond,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);

        using HttpResponseMessage imageResponse = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.Equal("3", imageResponse.Headers.GetValues("X-Trickplay-Frame-Index").Single());
        string entityTag = Assert.IsType<string>(imageResponse.Headers.ETag?.Tag);

        using HttpResponseMessage conditionalResponse = await fixture.GetConditionalAsync(entityTag);
        Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
        Assert.Equal("3", conditionalResponse.Headers.GetValues("X-Trickplay-Frame-Index").Single());
    }

    private static async Task<AuthorizationPolicy> GetPolicyAsync(
        PreviewHostFixture fixture,
        string actionName)
    {
        EndpointDataSource dataSource = fixture.Services.GetRequiredService<EndpointDataSource>();
        Endpoint endpoint = Assert.Single(
            dataSource.Endpoints,
            candidate => candidate.Metadata.GetMetadata<ControllerActionDescriptor>()?.MethodInfo.Name == actionName);
        IAuthorizationPolicyProvider provider = fixture.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        AuthorizationPolicy? policy = await AuthorizationPolicy.CombineAsync(
            provider,
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        return Assert.IsType<AuthorizationPolicy>(policy);
    }

}
