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

public sealed class TrickplayGeneratedMetadataScopeHttpSpecs
{
    [Fact]
    public async Task OperationalGetFailureDoesNotRenewAnOlderPositiveObservation()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(initial, 0);

        scenario.Time.Advance(TimeSpan.FromMinutes(29));
        scenario.QueueMetadataFailure();
        using HttpResponseMessage failedGet = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, failedGet.StatusCode);
        Assert.Equal(2, scenario.MetadataReadCount);

        using HttpResponseMessage retainedHead = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(retainedHead, 0);
        Assert.Equal(2, scenario.MetadataReadCount);

        scenario.Time.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage refreshedHead = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshedHead, 0);
        Assert.Equal(3, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task SelectedWidthAbsenceKeepsAnotherObservedResolutionUsable()
    {
        int[] configuredTargets = [320];
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = configuredTargets,
            Metadata = MetadataAvailability.ExactWidthMissing,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage missingSelectedWidth = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(missingSelectedWidth, HttpStatusCode.NotFound);
        Assert.Equal(1, scenario.MetadataReadCount);

        configuredTargets[0] = 640;
        using HttpResponseMessage availableObservedWidth = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(availableObservedWidth, 0);
        Assert.Equal(1, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task KeepsGeneratedMetadataSeparateForEachEffectiveSourceVideo()
    {
        var scenario = new PreviewScenario
        {
            RequestPositionTicks = 30_000L * TimeSpan.TicksPerMillisecond,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage defaultSource = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(defaultSource, 3);

        scenario.UsesAlternateSource = true;
        scenario.Metadata = MetadataAvailability.ChangedInterval;
        using HttpResponseMessage alternateSource = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(alternateSource, 1);
        Assert.Equal(2, scenario.MetadataReadCount);

        scenario.UsesAlternateSource = false;
        using HttpResponseMessage retainedDefaultSource = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(retainedDefaultSource, 3);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task WholeSourceAbsenceAppliesAcrossSelectedWidths()
    {
        int[] configuredTargets = [320];
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = configuredTargets,
            Metadata = MetadataAvailability.GeneratedMetadataMissing,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initialWidthAbsence = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(initialWidthAbsence, HttpStatusCode.NotFound);
        configuredTargets[0] = 640;

        using HttpResponseMessage alternateWidthAbsence = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(alternateWidthAbsence, HttpStatusCode.NotFound);
        Assert.Equal(1, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task NewerWholeSourceAbsenceReplacesAnOlderPositiveAtAnotherWidth()
    {
        int[] configuredTargets = [640];
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = configuredTargets,
            Metadata = MetadataAvailability.ExactWidthMissing,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage observedWidthPositive = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(observedWidthPositive, 0);

        scenario.Time.Advance(TimeSpan.FromMinutes(5));
        configuredTargets[0] = 320;
        scenario.Metadata = MetadataAvailability.GeneratedMetadataMissing;
        using HttpResponseMessage deletedSource = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.NotFound, deletedSource.StatusCode);
        Assert.Equal(2, scenario.MetadataReadCount);

        configuredTargets[0] = 640;
        using HttpResponseMessage retainedAbsence = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(retainedAbsence, HttpStatusCode.NotFound);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task SourceWideAbsenceRemainsUsableWhileAnOlderWidthReadIsInProgress()
    {
        int[] configuredTargets = [640];
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = configuredTargets,
            Metadata = MetadataAvailability.ExactWidthMissing,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage retainedWidthPositive = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(retainedWidthPositive, 0);

        scenario.Time.Advance(TimeSpan.FromMinutes(5));
        configuredTargets[0] = 320;
        MetadataReadPlan older = scenario.QueueBlockedMetadataRead(MetadataAvailability.Available);
        scenario.QueueMetadataRead(MetadataAvailability.GeneratedMetadataMissing);
        Task<HttpResponseMessage> olderWidthRead = fixture.HeadAsync();
        await older.Started.WaitAsync(TimeSpan.FromSeconds(10));

        using HttpResponseMessage newerAbsence = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.NotFound, newerAbsence.StatusCode);
        Assert.Equal(3, scenario.MetadataReadCount);

        configuredTargets[0] = 640;
        using HttpResponseMessage currentAbsence = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(currentAbsence, HttpStatusCode.NotFound);
        Assert.Equal(3, scenario.MetadataReadCount);

        older.Release();
        using HttpResponseMessage olderResponse = await olderWidthRead;
        await AssertTrickplayFrameProbeSuccessAsync(olderResponse, 0);
    }

    [Theory]
    [InlineData(MetadataAvailability.NoThumbnails)]
    [InlineData(MetadataAvailability.NegativeThumbnails)]
    public async Task ReusesSelectedWidthNoThumbnailsForFiveMinutes(
        MetadataAvailability unavailableMetadata)
    {
        var scenario = new PreviewScenario { Metadata = unavailableMetadata };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(initial, HttpStatusCode.NotFound);

        scenario.Time.Advance(TimeSpan.FromMinutes(4));
        scenario.Metadata = MetadataAvailability.Available;
        using HttpResponseMessage cached = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(cached, HttpStatusCode.NotFound);
        Assert.Equal(1, scenario.MetadataReadCount);

        scenario.Time.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage refreshed = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshed, 0);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task NewerInvalidObservationPreventsAnOlderReadFromResurrectingMetadata()
    {
        var scenario = new PreviewScenario();
        MetadataReadPlan older = scenario.QueueBlockedMetadataRead(MetadataAvailability.Available);
        scenario.QueueMetadataRead(MetadataAvailability.FrameWidthZero);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        Task<HttpResponseMessage> olderRequest = fixture.HeadAsync();
        await older.Started.WaitAsync(TimeSpan.FromSeconds(10));

        using HttpResponseMessage newerInvalid = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, newerInvalid.StatusCode);

        older.Release();
        using HttpResponseMessage olderResponse = await olderRequest;
        await AssertTrickplayFrameProbeSuccessAsync(olderResponse, 0);

        scenario.Metadata = MetadataAvailability.ChangedInterval;
        using HttpResponseMessage refreshed = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshed, 0);
        Assert.Equal(3, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task FailedRegenerationAfterDeletionNeverFallsBackToOldPositiveMetadata()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage generated = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(generated, 0);

        scenario.Metadata = MetadataAvailability.GeneratedMetadataMissing;
        using HttpResponseMessage deleted = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);

        using HttpResponseMessage retainedDeletion = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(retainedDeletion, HttpStatusCode.NotFound);
        Assert.Equal(2, scenario.MetadataReadCount);

        scenario.Time.Advance(TimeSpan.FromMinutes(5));
        scenario.QueueMetadataFailure();
        using HttpResponseMessage failedRegeneration = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(
            failedRegeneration,
            HttpStatusCode.InternalServerError);
        Assert.Equal(3, scenario.MetadataReadCount);
    }

}
