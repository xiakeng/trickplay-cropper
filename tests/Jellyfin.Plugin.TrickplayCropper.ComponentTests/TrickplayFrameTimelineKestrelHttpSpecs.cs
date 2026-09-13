using System.Net;
using System.Net.Http.Headers;
using Xunit;

using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.PreviewHttpTestValues;
using static Jellyfin.Plugin.TrickplayCropper.ComponentTests.TrickplayPreviewHttpSupport;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayFrameTimelineKestrelHttpSpecs
{
    [Fact]
    public async Task ReturnsTheExactAuthoritativeTimelineWithoutImageWork()
    {
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = [640, 320],
            MetadataIntervalMilliseconds = 300_000,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/TrickplayCropper/Videos/{ItemId:D}/FrameTimeline");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"ignored\""));

        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "{\"intervalTicks\":3000000000,\"frameCount\":4}",
            await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Content.Headers.LastModified);
        Assert.False(response.Headers.Contains("X-Trickplay-Frame-Index"));
        Assert.False(response.Headers.Contains("X-Trickplay-Cache"));
        Assert.False(response.Headers.Contains("Server-Timing"));
        Assert.Equal(1, scenario.MetadataReadCount);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Empty(fixture.EnumerateCacheFiles());
    }

    [Fact]
    public async Task ReturnsAnAuthorizedAlternateSourceAtItsClampedEvenResolution()
    {
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = [640],
            DeniesSelectedVideoPlayback = true,
            SourceVideoWidth = 321,
            UsesAlternateSource = true,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);

        using HttpResponseMessage response = await GetTimelineAsync(fixture, scenario);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "{\"intervalTicks\":100000000,\"frameCount\":4}",
            await response.Content.ReadAsStringAsync());
        Assert.Equal([ItemId, AlternateSourceId], scenario.LibraryLookupIds);
        Assert.Equal(1, scenario.MetadataReadCount);
    }

    [Theory]
    [InlineData(MetadataAvailability.FrameHeightZero)]
    [InlineData(MetadataAvailability.TileWidthZero)]
    [InlineData(MetadataAvailability.TileHeightZero)]
    public async Task IgnoresPreviewOnlyMetadata(MetadataAvailability metadata)
    {
        var scenario = new PreviewScenario { Metadata = metadata };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);

        using HttpResponseMessage response = await GetTimelineAsync(fixture, scenario);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "{\"intervalTicks\":100000000,\"frameCount\":4}",
            await response.Content.ReadAsStringAsync());
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
    }

    [Theory]
    [InlineData(FrameTimelineFailure.Unauthenticated, HttpStatusCode.Unauthorized)]
    [InlineData(FrameTimelineFailure.UserIdentityMissing, HttpStatusCode.Unauthorized)]
    [InlineData(FrameTimelineFailure.UserlessApiKey, HttpStatusCode.Forbidden)]
    [InlineData(FrameTimelineFailure.DefaultPolicyDenied, HttpStatusCode.Forbidden)]
    [InlineData(FrameTimelineFailure.PlaybackDenied, HttpStatusCode.Forbidden)]
    [InlineData(FrameTimelineFailure.LogicalVideoHidden, HttpStatusCode.NotFound)]
    [InlineData(FrameTimelineFailure.SourceNotMember, HttpStatusCode.NotFound)]
    [InlineData(FrameTimelineFailure.SourceVideoHidden, HttpStatusCode.NotFound)]
    [InlineData(FrameTimelineFailure.NoConfiguredTarget, HttpStatusCode.NotFound)]
    [InlineData(FrameTimelineFailure.NoGeneratedMetadata, HttpStatusCode.NotFound)]
    [InlineData(FrameTimelineFailure.ExactMetadataMissing, HttpStatusCode.NotFound)]
    [InlineData(FrameTimelineFailure.InvalidConfiguration, HttpStatusCode.InternalServerError)]
    [InlineData(FrameTimelineFailure.ContradictoryWidth, HttpStatusCode.InternalServerError)]
    [InlineData(FrameTimelineFailure.NonPositiveInterval, HttpStatusCode.InternalServerError)]
    [InlineData(FrameTimelineFailure.ZeroFrameCount, HttpStatusCode.InternalServerError)]
    [InlineData(FrameTimelineFailure.NegativeFrameCount, HttpStatusCode.InternalServerError)]
    [InlineData(FrameTimelineFailure.MetadataReadFailure, HttpStatusCode.InternalServerError)]
    public async Task MapsAuthorizationAvailabilityAndServerFailures(
        FrameTimelineFailure failure,
        HttpStatusCode expectedStatus)
    {
        PreviewScenario scenario = CreateFailureScenario(failure);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);

        using HttpResponseMessage response = await GetTimelineAsync(fixture, scenario);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
    }

    [Theory]
    [InlineData("/TrickplayCropper/Videos/not-a-guid/FrameTimeline")]
    [InlineData("/TrickplayCropper/Videos/8b73d6c2-4a15-4a53-aef6-521758be6bf4/FrameTimeline?MediaSourceId=not-a-guid")]
    public async Task RejectsMalformedGuidBindingBeforeApplicationWork(string requestPath)
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);

        using HttpResponseMessage response = await fixture.Client.GetAsync(requestPath);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, scenario.UserLookups);
        Assert.Equal(0, scenario.MetadataReadCount);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
    }

    [Fact]
    public async Task DoesNotPublishSourceOrMetadataObservations()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);

        using HttpResponseMessage timelineResponse = await GetTimelineAsync(fixture, scenario);
        using HttpResponseMessage probeResponse = await fixture.HeadAsync();

        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        await AssertTrickplayFrameProbeSuccessAsync(probeResponse, 0);
        Assert.Equal(2, scenario.MetadataReadCount);
        Assert.Equal(2, scenario.UserIndependentLibraryLookups);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);
    }

    [Fact]
    public async Task DoesNotReuseExistingSourceOrMetadataObservations()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(scenario);

        using HttpResponseMessage probeResponse = await fixture.HeadAsync();
        using HttpResponseMessage timelineResponse = await GetTimelineAsync(fixture, scenario);

        await AssertTrickplayFrameProbeSuccessAsync(probeResponse, 0);
        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        Assert.Equal(2, scenario.MetadataReadCount);
        Assert.Equal(2, scenario.UserScopedLibraryLookups);
        Assert.Equal(1, scenario.UserScopedSourceEnumerations);
    }

    private static Task<HttpResponseMessage> GetTimelineAsync(
        PreviewHostFixture fixture,
        PreviewScenario scenario)
    {
        string source = scenario.UsesAlternateSource
            ? $"?MediaSourceId={AlternateSourceId:D}"
            : string.Empty;
        return fixture.Client.GetAsync(
            $"/TrickplayCropper/Videos/{scenario.LogicalItemId:D}/FrameTimeline{source}");
    }

    private static PreviewScenario CreateFailureScenario(FrameTimelineFailure failure)
    {
        PreviewScenario scenario = failure switch
        {
            FrameTimelineFailure.Unauthenticated => new PreviewScenario
            {
                Authentication = AuthenticationState.Missing,
            },
            FrameTimelineFailure.UserIdentityMissing => new PreviewScenario
            {
                Authentication = AuthenticationState.MissingUserId,
            },
            FrameTimelineFailure.UserlessApiKey => new PreviewScenario
            {
                Authentication = AuthenticationState.ApiKeyWithoutCurrentUser,
            },
            FrameTimelineFailure.DefaultPolicyDenied => new PreviewScenario
            {
                DeniesDefaultAuthorizationPolicy = true,
            },
            FrameTimelineFailure.PlaybackDenied => new PreviewScenario
            {
                DeniesLogicalVideoPlayback = true,
            },
            FrameTimelineFailure.LogicalVideoHidden => new PreviewScenario
            {
                LogicalVideo = ItemAvailability.Hidden,
            },
            FrameTimelineFailure.SourceNotMember => new PreviewScenario
            {
                Membership = SourceMembership.NotMember,
                UsesAlternateSource = true,
            },
            FrameTimelineFailure.SourceVideoHidden => new PreviewScenario
            {
                SelectedVideo = ItemAvailability.Hidden,
                UsesAlternateSource = true,
            },
            FrameTimelineFailure.NoConfiguredTarget => new PreviewScenario
            {
                ConfiguredWidthResolutions = [],
            },
            FrameTimelineFailure.NoGeneratedMetadata => new PreviewScenario
            {
                Metadata = MetadataAvailability.GeneratedMetadataMissing,
            },
            FrameTimelineFailure.ExactMetadataMissing => new PreviewScenario
            {
                Metadata = MetadataAvailability.ExactWidthMissing,
            },
            FrameTimelineFailure.InvalidConfiguration => new PreviewScenario
            {
                ConfiguredWidthResolutions = [320, 0],
            },
            FrameTimelineFailure.ContradictoryWidth => new PreviewScenario
            {
                Metadata = MetadataAvailability.ContradictoryFrameWidth,
            },
            FrameTimelineFailure.NonPositiveInterval => new PreviewScenario
            {
                Metadata = MetadataAvailability.IntervalZero,
            },
            FrameTimelineFailure.ZeroFrameCount => new PreviewScenario
            {
                Metadata = MetadataAvailability.NoThumbnails,
            },
            FrameTimelineFailure.NegativeFrameCount => new PreviewScenario
            {
                Metadata = MetadataAvailability.NegativeThumbnails,
            },
            FrameTimelineFailure.MetadataReadFailure => new PreviewScenario(),
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "Unknown Timeline failure."),
        };
        if (failure == FrameTimelineFailure.MetadataReadFailure)
        {
            scenario.QueueMetadataFailure();
        }

        return scenario;
    }

    public enum FrameTimelineFailure
    {
        Unauthenticated,
        UserIdentityMissing,
        UserlessApiKey,
        DefaultPolicyDenied,
        PlaybackDenied,
        LogicalVideoHidden,
        SourceNotMember,
        SourceVideoHidden,
        NoConfiguredTarget,
        NoGeneratedMetadata,
        ExactMetadataMissing,
        InvalidConfiguration,
        ContradictoryWidth,
        NonPositiveInterval,
        ZeroFrameCount,
        NegativeFrameCount,
        MetadataReadFailure,
    }
}
