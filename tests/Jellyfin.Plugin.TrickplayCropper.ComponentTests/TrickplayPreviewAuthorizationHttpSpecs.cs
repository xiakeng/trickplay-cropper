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

public sealed class TrickplayPreviewAuthorizationHttpSpecs
{
    [Theory]
    [InlineData(AuthenticationState.Missing)]
    [InlineData(AuthenticationState.Invalid)]
    [InlineData(AuthenticationState.UnusableUserSession)]
    public async Task RejectsUnusableUserSession(AuthenticationState authentication)
    {
        var scenario = new PreviewScenario { Authentication = authentication };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertAuthorizationErrorResponseAsync(response);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
    }

    [Fact]
    public async Task ForbidsLogicalVideoPlaybackDenial()
    {
        var scenario = new PreviewScenario { DeniesLogicalVideoPlayback = true };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertAuthorizationErrorResponseAsync(response);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
    }

    [Fact]
    public async Task AuthorizesAnAlternateSourceWithoutASecondSourceVideoPlaybackDecision()
    {
        var scenario = new PreviewScenario
        {
            DeniesSelectedVideoPlayback = true,
            UsesAlternateSource = true,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("MISS", response.Headers.GetValues("X-Trickplay-Cache").Single());
        Assert.Single(fixture.Cache.Identities);
        Assert.StartsWith(
            string.Concat(AlternateSourceId.ToString("N"), Path.DirectorySeparatorChar),
            fixture.Cache.Identities[0].RelativePath,
            StringComparison.Ordinal);
        Assert.Equal(0, fixture.ErrorLogCount);
    }

    [Fact]
    public async Task ForbidsApiKeyWithoutCurrentUser()
    {
        var scenario = new PreviewScenario
        {
            Authentication = AuthenticationState.ApiKeyWithoutCurrentUser,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertAuthorizationErrorResponseAsync(response);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
    }

    [Fact]
    public async Task ForbidsDefaultAuthorizationPolicyDenial()
    {
        var scenario = new PreviewScenario { DeniesDefaultAuthorizationPolicy = true };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertAuthorizationErrorResponseAsync(response);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
    }

    [Theory]
    [InlineData(NotFoundCondition.LogicalVideoMissing)]
    [InlineData(NotFoundCondition.LogicalVideoHidden)]
    [InlineData(NotFoundCondition.LogicalItemWrongType)]
    [InlineData(NotFoundCondition.SelectedSourceNotMember)]
    [InlineData(NotFoundCondition.SelectedSourceMembershipMalformed)]
    [InlineData(NotFoundCondition.SelectedVideoMissing)]
    [InlineData(NotFoundCondition.SelectedVideoIdentityMismatch)]
    [InlineData(NotFoundCondition.SelectedVideoHidden)]
    [InlineData(NotFoundCondition.SelectedItemWrongType)]
    [InlineData(NotFoundCondition.NoConfiguredTarget)]
    [InlineData(NotFoundCondition.GeneratedMetadataMissing)]
    [InlineData(NotFoundCondition.ExactMetadataMissing)]
    [InlineData(NotFoundCondition.ThumbnailsMissing)]
    [InlineData(NotFoundCondition.ThumbnailsNegative)]
    public async Task ConcealsUnavailableResourceWithoutGetOnlyWork(NotFoundCondition condition)
    {
        PreviewScenario scenario = CreateNotFoundScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemDetailsResponseAsync(response);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
        AssertExpectedUnavailableDebugReason(fixture.DebugLogs, condition);
    }

    [Theory]
    [InlineData(NotFoundCondition.ManagerPathMissing)]
    [InlineData(NotFoundCondition.SourceSpriteMissing)]
    public async Task ConcealsMissingSourceSpriteAfterASuccessfulSharedContext(NotFoundCondition condition)
    {
        PreviewScenario scenario = CreateNotFoundScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemDetailsResponseAsync(response);
        Assert.Equal(1, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
        AssertExpectedUnavailableDebugReason(fixture.DebugLogs, condition);
    }

    [Fact]
    public async Task AuthorizesBeforeReadingSharedPreviewCacheEntry()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();
        using HttpResponseMessage authorizedResponse = await fixture.GetAsync();
        string entityTag = Assert.IsType<string>(authorizedResponse.Headers.ETag?.Tag);
        Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
        Assert.Equal(1, fixture.Cache.CallCount);
        Assert.Single(Directory.EnumerateFiles(fixture.CacheRoot, "*.jpg", SearchOption.AllDirectories));

        fixture.SetPlaybackAccess(false);
        using HttpResponseMessage deniedResponse = await fixture.GetConditionalAsync(entityTag);

        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        await AssertAuthorizationErrorResponseAsync(deniedResponse);
        Assert.Equal(1, fixture.Cache.CallCount);
        Assert.Single(Directory.EnumerateFiles(fixture.CacheRoot, "*.jpg", SearchOption.AllDirectories));
        Assert.Equal(0, fixture.ErrorLogCount);
    }

    [Theory]
    [MemberData(nameof(MalformedPreviewRequestPaths), MemberType = typeof(TrickplayPreviewHttpSupport))]
    public async Task RejectsMissingMalformedAndNegativeRequestValues(string requestPath)
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync(requestPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsResponseAsync(response);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
    }

}
