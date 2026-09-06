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

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayGeneratedMetadataObservationHttpSpecs : TrickplayPreviewHttpSharedSpecs
{
    [Fact]
    public async Task GetRefreshesPositiveMetadataForImageHitsAndConditionalRequests()
    {
        var scenario = new PreviewScenario
        {
            RequestPositionTicks = 30_000L * TimeSpan.TicksPerMillisecond,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage generated = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        Assert.Equal("MISS", generated.Headers.GetValues("X-Trickplay-Cache").Single());
        string entityTag = Assert.IsType<string>(generated.Headers.ETag?.Tag);
        Assert.Equal(1, scenario.MetadataReadCount);

        scenario.Time.Advance(TimeSpan.FromMinutes(10));
        using HttpResponseMessage imageHit = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.OK, imageHit.StatusCode);
        Assert.Equal("HIT", imageHit.Headers.GetValues("X-Trickplay-Cache").Single());
        Assert.Equal(2, scenario.MetadataReadCount);

        scenario.Time.Advance(TimeSpan.FromMinutes(10));
        using HttpResponseMessage conditional = await fixture.GetConditionalAsync(entityTag);
        Assert.Equal(HttpStatusCode.NotModified, conditional.StatusCode);
        Assert.Equal(3, scenario.MetadataReadCount);

        scenario.Metadata = MetadataAvailability.ChangedInterval;
        scenario.Time.Advance(TimeSpan.FromMinutes(29));
        using HttpResponseMessage renewedHead = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(renewedHead, 3);
        Assert.Equal(3, scenario.MetadataReadCount);

        scenario.Time.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage expiredHead = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(expiredHead, 1);
        Assert.Equal(4, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task GetPublishesItsCurrentFrameIndexOverAnOlderHeadObservation()
    {
        var scenario = new PreviewScenario
        {
            RequestPositionTicks = 30_000L * TimeSpan.TicksPerMillisecond,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage oldHead = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(oldHead, 3);

        scenario.Metadata = MetadataAvailability.ChangedInterval;
        using HttpResponseMessage stillOldHead = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(stillOldHead, 3);
        Assert.Equal(1, scenario.MetadataReadCount);

        using HttpResponseMessage currentGet = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.OK, currentGet.StatusCode);
        Assert.Equal("1", currentGet.Headers.GetValues("X-Trickplay-Frame-Index").Single());
        Assert.Equal(2, scenario.MetadataReadCount);

        using HttpResponseMessage currentHead = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(currentHead, 1);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task MissingWidthDerivedFromAPositiveObservationKeepsTheOriginalFiveMinuteAge()
    {
        int[] configuredTargets = [640];
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = configuredTargets,
            Metadata = MetadataAvailability.ExactWidthMissing,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage availableWidth = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(availableWidth, 0);
        Assert.Equal(1, scenario.MetadataReadCount);

        scenario.Time.Advance(TimeSpan.FromMinutes(4));
        configuredTargets[0] = 320;
        using HttpResponseMessage derivedAbsence = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(derivedAbsence, HttpStatusCode.NotFound);
        Assert.Equal(1, scenario.MetadataReadCount);

        scenario.Metadata = MetadataAvailability.Available;
        scenario.Time.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage refreshedWidth = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshedWidth, 0);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task NewerAbsenceCannotBeOverwrittenByAnOlderPositiveRead()
    {
        var scenario = new PreviewScenario();
        MetadataReadPlan older = scenario.QueueBlockedMetadataRead(MetadataAvailability.Available);
        scenario.QueueMetadataRead(MetadataAvailability.GeneratedMetadataMissing);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        Task<HttpResponseMessage> olderRequest = fixture.HeadAsync();
        await older.Started.WaitAsync(TimeSpan.FromSeconds(10));

        using HttpResponseMessage newerResponse = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.NotFound, newerResponse.StatusCode);
        Assert.Equal(2, scenario.MetadataReadCount);

        older.Release();
        using HttpResponseMessage olderResponse = await olderRequest;
        await AssertTrickplayFrameProbeSuccessAsync(olderResponse, 0);

        using HttpResponseMessage retainedNewerAbsence = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(
            retainedNewerAbsence,
            HttpStatusCode.NotFound);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task DifferentSelectedWidthsCanReadIndependently()
    {
        int[] configuredTargets = [320];
        var scenario = new PreviewScenario { ConfiguredWidthResolutions = configuredTargets };
        MetadataReadPlan first = scenario.QueueBlockedMetadataRead(MetadataAvailability.Available);
        scenario.QueueMetadataRead(MetadataAvailability.ExactWidthMissing);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        Task<HttpResponseMessage> initialWidthRequest = fixture.HeadAsync();
        await first.Started.WaitAsync(TimeSpan.FromSeconds(10));
        configuredTargets[0] = 640;

        using HttpResponseMessage alternateWidthResponse = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(alternateWidthResponse, 0);
        Assert.Equal(2, scenario.MetadataReadCount);

        first.Release();
        using HttpResponseMessage completedInitialWidth = await initialWidthRequest;
        await AssertTrickplayFrameProbeSuccessAsync(completedInitialWidth, 0);
    }

    [Fact]
    public async Task ConcurrentFailureDoesNotRetireStateNeededBySuccessfulRead()
    {
        var scenario = new PreviewScenario();
        MetadataReadPlan failing = scenario.QueueBlockedMetadataFailure();
        MetadataReadPlan successful = scenario.QueueBlockedMetadataRead(MetadataAvailability.Available);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        Task<HttpResponseMessage> failingRequest = fixture.HeadAsync();
        await failing.Started.WaitAsync(TimeSpan.FromSeconds(10));
        Task<HttpResponseMessage> successfulRequest = fixture.HeadAsync();
        await successful.Started.WaitAsync(TimeSpan.FromSeconds(10));

        failing.Release();
        using HttpResponseMessage failure = await failingRequest;
        await AssertBodylessTrickplayFrameProbeFailureAsync(failure, HttpStatusCode.InternalServerError);

        successful.Release();
        using HttpResponseMessage success = await successfulRequest;
        await AssertTrickplayFrameProbeSuccessAsync(success, 0);

        scenario.Metadata = MetadataAvailability.ChangedInterval;
        using HttpResponseMessage retainedSuccess = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(retainedSuccess, 0);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task InvalidGetMetadataRemovesAnOlderPositiveObservation()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(initial, 0);

        scenario.Metadata = MetadataAvailability.FrameWidthZero;
        using HttpResponseMessage invalid = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, invalid.StatusCode);
        Assert.Equal(2, scenario.MetadataReadCount);

        scenario.Metadata = MetadataAvailability.ChangedInterval;
        using HttpResponseMessage recovered = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(recovered, 0);
        Assert.Equal(3, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task CanceledMetadataReadPublishesWhenTheIssuedHostQueryLaterSucceeds()
    {
        var scenario = new PreviewScenario();
        MetadataReadPlan pending = scenario.QueueBlockedMetadataRead(MetadataAvailability.Available);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using var cancellation = new CancellationTokenSource();

        Task<HttpResponseMessage> canceledRequest = fixture.HeadAsync(cancellation.Token);
        await pending.Started.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledRequest);

        pending.Release();
        await pending.Completed.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Yield();
        scenario.Metadata = MetadataAvailability.ChangedInterval;

        using HttpResponseMessage cached = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(cached, 0);
        Assert.Equal(1, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task DoesNotServeOrPublishMetadataThatExpiresBeforeItsReadCompletes()
    {
        var scenario = new PreviewScenario
        {
            RequestPositionTicks = 30_000L * TimeSpan.TicksPerMillisecond,
        };
        MetadataReadPlan pending = scenario.QueueBlockedMetadataRead(MetadataAvailability.Available);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        Task<HttpResponseMessage> expiredRequest = fixture.HeadAsync();
        await pending.Started.WaitAsync(TimeSpan.FromSeconds(10));
        scenario.Time.Advance(TimeSpan.FromMinutes(30));
        pending.Release();

        using HttpResponseMessage expired = await expiredRequest;
        await AssertBodylessTrickplayFrameProbeFailureAsync(expired, HttpStatusCode.InternalServerError);

        scenario.Metadata = MetadataAvailability.ChangedInterval;
        using HttpResponseMessage refreshed = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshed, 1);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

}
