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

public sealed class TrickplayFrameProbeHttpSpecs
{
    [Fact]
    public async Task ServesBodylessTrickplayFrameProbeSuccessWithExactlyTwoPluginHeaders()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertTrickplayFrameProbeSuccessAsync(response, 0);
    }

    [Fact]
    public async Task ReusesPositiveGeneratedMetadataForThirtyMinutesWithoutSliding()
    {
        var scenario = new PreviewScenario
        {
            RequestPositionTicks = 30_000L * TimeSpan.TicksPerMillisecond,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(initial, 3);
        Assert.Equal(1, scenario.MetadataReadCount);

        scenario.Metadata = MetadataAvailability.ChangedInterval;
        scenario.Time.Advance(TimeSpan.FromMinutes(29));
        using HttpResponseMessage sustainedHit = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(sustainedHit, 3);
        Assert.Equal(1, scenario.MetadataReadCount);

        scenario.Time.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage refreshed = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshed, 1);
        Assert.Equal(2, scenario.MetadataReadCount);
        Assert.Equal(2, scenario.UserIndependentSourceEnumerations);
    }

    [Fact]
    public async Task CalculatesEachPositionFromTheSameMetadataObservation()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage firstPosition = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(firstPosition, 0);

        scenario.RequestPositionTicks = 30_000L * TimeSpan.TicksPerMillisecond;
        using HttpResponseMessage laterPosition = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(laterPosition, 3);
        Assert.Equal(1, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task WarmProbePerformsNoPluginSourceOrMetadataIo()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage cold = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(cold, 0);
        Assert.Equal(2, scenario.UserIndependentLibraryLookups);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(1, scenario.MetadataReadCount);

        using HttpResponseMessage warm = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(warm, 0);
        Assert.Equal(2, scenario.UserIndependentLibraryLookups);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(1, scenario.MetadataReadCount);
        Assert.Equal(2, scenario.NativeAuthenticationUserLoads);
        Assert.Equal(0, scenario.DefaultAuthorizationUserLoads);
        Assert.Equal(0, scenario.UserLookups);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(9_999L, 0)]
    [InlineData(10_000L, 1)]
    [InlineData(15_000L, 1)]
    [InlineData(39_999L, 3)]
    [InlineData(40_000L, 3)]
    [InlineData(100_000L, 3)]
    public async Task ClampsTheTrickplayFrameProbeIndexToTheGeneratedFrameSequence(
        long positionMilliseconds,
        int expectedFrameIndex)
    {
        var scenario = new PreviewScenario
        {
            RequestPositionTicks = positionMilliseconds * TimeSpan.TicksPerMillisecond,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertTrickplayFrameProbeSuccessAsync(response, expectedFrameIndex);
    }

    [Fact]
    public async Task ServesTrickplayFrameProbeForTheAlternateMediaSource()
    {
        var scenario = new PreviewScenario { UsesAlternateSource = true };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertTrickplayFrameProbeSuccessAsync(response, 0);
    }

    [Theory]
    [InlineData(HostSourceKind.Default)]
    [InlineData(HostSourceKind.LocalAlternate)]
    [InlineData(HostSourceKind.LinkedAlternate)]
    [InlineData(HostSourceKind.EligibleDynamic)]
    public async Task ProbesEverySupportedSourceKindReturnedByFullHostEnumeration(HostSourceKind sourceKind)
    {
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = [640],
            HostSource = sourceKind,
            SourceVideoWidth = 321,
            UsesAlternateSource = sourceKind != HostSourceKind.Default,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertTrickplayFrameProbeSuccessAsync(response, 0);
        using HttpResponseMessage warm = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(warm, 0);
        Assert.Equal(2, scenario.NativeAuthenticationUserLoads);
        Assert.Equal(0, scenario.DefaultAuthorizationUserLoads);
        Assert.Equal(0, scenario.UserLookups);
        Assert.Equal(0, scenario.UserScopedSourceEnumerations);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(2, scenario.UserIndependentLibraryLookups);
        Assert.Equal(1, scenario.MetadataReadCount);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
    }

    [Fact]
    public async Task SucceedsTrickplayFrameProbeWithoutAResolvableSourceSprite()
    {
        var scenario = new PreviewScenario { SourceSprite = SourceSpriteAvailability.FileMissing };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage probeResponse = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(probeResponse, 0);

        using HttpResponseMessage getResponse = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        using HttpResponseMessage retainedProbeResponse = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(retainedProbeResponse, 0);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

    [Theory]
    [InlineData(InternalFailureCondition.CropXOverflow, 7_000_000)]
    [InlineData(InternalFailureCondition.CropYOverflow, 2)]
    [InlineData(InternalFailureCondition.CropRightOverflow, 6_710_886)]
    [InlineData(InternalFailureCondition.CropBottomOverflow, 1)]
    public async Task SucceedsTrickplayFrameProbeWhenGetOnlyCropGeometryWouldOverflow(
        InternalFailureCondition condition,
        int expectedFrameIndex)
    {
        PreviewScenario scenario = CreateInternalFailureScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertTrickplayFrameProbeSuccessAsync(response, expectedFrameIndex);
    }

    [Theory]
    [MemberData(nameof(MalformedPreviewRequestPaths), MemberType = typeof(TrickplayPreviewHttpSupport))]
    public async Task RejectsMissingMalformedAndNegativeTrickplayFrameProbeValues(string requestPath)
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.HeadRawAsync(requestPath);

        await AssertBodylessTrickplayFrameProbeFailureAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TreatsAnEmptyTrickplayFrameProbeMediaSourceAsUnspecified()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.HeadRawAsync(
            $"{PreviewPath}?MediaSourceId=&PositionTicks=0");

        await AssertTrickplayFrameProbeSuccessAsync(response, 0);
    }

    [Theory]
    [InlineData(AuthenticationState.Missing)]
    [InlineData(AuthenticationState.Invalid)]
    [InlineData(AuthenticationState.UnusableUserSession)]
    public async Task RejectsUnusableTrickplayFrameProbeSession(AuthenticationState authentication)
    {
        var scenario = new PreviewScenario { Authentication = authentication };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertBodylessTrickplayFrameProbeFailureAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AllowsApiKeyWithoutCurrentUserToProbeButNotFetchThePreview()
    {
        var scenario = new PreviewScenario
        {
            Authentication = AuthenticationState.ApiKeyWithoutCurrentUser,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage probeResponse = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(probeResponse, 0);
        Assert.Equal(0, scenario.UserLookups);
        Assert.Equal(0, scenario.UserScopedLibraryLookups);
        Assert.Equal(0, scenario.UserScopedSourceEnumerations);
        Assert.Equal(2, scenario.UserIndependentLibraryLookups);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);

        using HttpResponseMessage previewResponse = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.Forbidden, previewResponse.StatusCode);
        await AssertAuthorizationErrorResponseAsync(previewResponse);
        Assert.Equal(1, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task IgnoresTrickplayFrameProbeDefaultAuthorizationPolicyDenial()
    {
        var scenario = new PreviewScenario { DeniesDefaultAuthorizationPolicy = true };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertTrickplayFrameProbeSuccessAsync(response, 0);
    }

    [Fact]
    public async Task AllowsPlaybackDeniedUserToProbeButNotFetchThePreview()
    {
        var scenario = new PreviewScenario { DeniesLogicalVideoPlayback = true };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage probeResponse = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(probeResponse, 0);
        Assert.Equal(0, scenario.UserLookups);
        Assert.Equal(0, scenario.UserScopedLibraryLookups);
        Assert.Equal(0, scenario.UserScopedSourceEnumerations);

        using HttpResponseMessage previewResponse = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.Forbidden, previewResponse.StatusCode);
        await AssertAuthorizationErrorResponseAsync(previewResponse);
    }

    [Theory]
    [InlineData(NotFoundCondition.LogicalVideoHidden)]
    [InlineData(NotFoundCondition.SelectedVideoHidden)]
    public async Task AllowsInvisibleGeneratedMediaToBeProbedButNotFetched(NotFoundCondition condition)
    {
        PreviewScenario scenario = CreateNotFoundScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage probeResponse = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(probeResponse, 0);
        Assert.Equal(0, scenario.UserLookups);
        Assert.Equal(0, scenario.UserScopedLibraryLookups);
        Assert.Equal(0, scenario.UserScopedSourceEnumerations);

        using HttpResponseMessage previewResponse = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.NotFound, previewResponse.StatusCode);
        await AssertProblemDetailsResponseAsync(previewResponse);
        Assert.Equal(1, scenario.MetadataReadCount);
    }

    [Theory]
    [InlineData(NotFoundCondition.LogicalVideoMissing)]
    [InlineData(NotFoundCondition.LogicalItemWrongType)]
    [InlineData(NotFoundCondition.SelectedSourceNotMember)]
    [InlineData(NotFoundCondition.SelectedSourceMembershipMalformed)]
    [InlineData(NotFoundCondition.SelectedVideoMissing)]
    [InlineData(NotFoundCondition.SelectedVideoIdentityMismatch)]
    [InlineData(NotFoundCondition.SelectedItemWrongType)]
    [InlineData(NotFoundCondition.NoConfiguredTarget)]
    [InlineData(NotFoundCondition.GeneratedMetadataMissing)]
    [InlineData(NotFoundCondition.ExactMetadataMissing)]
    [InlineData(NotFoundCondition.ThumbnailsMissing)]
    [InlineData(NotFoundCondition.ThumbnailsNegative)]
    public async Task ConcealsUnavailableResourceFromTheTrickplayFrameProbe(NotFoundCondition condition)
    {
        PreviewScenario scenario = CreateNotFoundScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertBodylessTrickplayFrameProbeFailureAsync(response, HttpStatusCode.NotFound);
        AssertExpectedUnavailableDebugReason(fixture.ProbeDebugLogs, condition);
    }

    [Theory]
    [InlineData(InternalFailureCondition.ContradictoryFrameWidth)]
    [InlineData(InternalFailureCondition.FrameWidthZero)]
    [InlineData(InternalFailureCondition.FrameHeightZero)]
    [InlineData(InternalFailureCondition.IntervalZero)]
    [InlineData(InternalFailureCondition.TileWidthZero)]
    [InlineData(InternalFailureCondition.TileHeightZero)]
    public async Task ReportsInvalidTrickplayFrameProbeMetadataAsBodylessInternalError(InternalFailureCondition condition)
    {
        PreviewScenario scenario = CreateInternalFailureScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertBodylessTrickplayFrameProbeFailureAsync(response, HttpStatusCode.InternalServerError);
    }

    [Theory]
    [InlineData(ConfigurationFailureKind.UnreadableSnapshot)]
    [InlineData(ConfigurationFailureKind.NonPositiveConfiguredTarget)]
    [InlineData(ConfigurationFailureKind.NonPositiveSelectedResolution)]
    public async Task ReportsInvalidTrickplayFrameProbeConfigurationAsBodylessInternalError(ConfigurationFailureKind kind)
    {
        var scenario = new PreviewScenario { ConfiguredWidthResolutions = CreateInvalidConfiguration(kind) };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.HeadAsync();

        await AssertBodylessTrickplayFrameProbeFailureAsync(response, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task IgnoresTrickplayFrameProbeConditionalEntityTags()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();
        using HttpResponseMessage generatedResponse = await fixture.GetAsync();
        string entityTag = Assert.IsType<string>(generatedResponse.Headers.ETag?.Tag);

        using HttpResponseMessage exactResponse = await fixture.HeadConditionalAsync(entityTag);
        await AssertTrickplayFrameProbeSuccessAsync(exactResponse, 0);

        using HttpResponseMessage wildcardResponse = await fixture.HeadConditionalAsync("*");
        await AssertTrickplayFrameProbeSuccessAsync(wildcardResponse, 0);

        fixture.SetPlaybackAccess(false);
        using HttpResponseMessage deniedResponse = await fixture.HeadConditionalAsync(entityTag);
        await AssertTrickplayFrameProbeSuccessAsync(deniedResponse, 0);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    public async Task AdvertisesGetAndHeadForUnsupportedPreviewMethods(string method)
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.SendVerbAsync(method);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["GET", "HEAD"], response.Content.Headers.Allow);
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
    }

}
