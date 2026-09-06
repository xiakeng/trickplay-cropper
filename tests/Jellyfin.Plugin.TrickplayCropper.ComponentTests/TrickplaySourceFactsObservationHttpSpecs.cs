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

public sealed class TrickplaySourceFactsObservationHttpSpecs : TrickplayPreviewHttpSharedSpecs
{
    [Fact]
    public async Task SourceFactsExpireIndependentlyFromNewerMetadata()
    {
        int[] configuredTargets = [320];
        var scenario = new PreviewScenario { ConfiguredWidthResolutions = configuredTargets };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(initial, 0);

        scenario.Time.Advance(TimeSpan.FromMinutes(10));
        configuredTargets[0] = 640;
        scenario.Metadata = MetadataAvailability.ExactWidthMissing;
        using HttpResponseMessage newerMetadata = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(newerMetadata, 0);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(2, scenario.MetadataReadCount);

        scenario.Time.Advance(TimeSpan.FromMinutes(20));
        using HttpResponseMessage refreshedSource = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshedSource, 0);
        Assert.Equal(2, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(4, scenario.UserIndependentLibraryLookups);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task ExplicitSourceAbsenceExpiresAfterFiveMinutes()
    {
        var scenario = new PreviewScenario { LogicalVideo = ItemAvailability.Missing };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(initial, HttpStatusCode.NotFound);
        Assert.Equal(1, scenario.UserIndependentLibraryLookups);

        scenario.LogicalVideo = ItemAvailability.Available;
        scenario.Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        using HttpResponseMessage retainedAbsence = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(retainedAbsence, HttpStatusCode.NotFound);
        Assert.Equal(1, scenario.UserIndependentLibraryLookups);

        scenario.Time.Advance(TimeSpan.FromSeconds(1));
        using HttpResponseMessage refreshed = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshed, 0);
        Assert.Equal(3, scenario.UserIndependentLibraryLookups);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);
    }

    [Fact]
    public async Task SuccessfulGetWarmsUserNeutralSourceFactsForTheProbe()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage preview = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(1, scenario.UserLookups);
        Assert.Equal(2, scenario.UserScopedLibraryLookups);
        Assert.Equal(1, scenario.UserScopedSourceEnumerations);
        Assert.Equal(0, scenario.UserIndependentLibraryLookups);
        Assert.Equal(0, scenario.UserIndependentSourceEnumerations);

        using HttpResponseMessage probe = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(probe, 0);
        Assert.Equal(1, scenario.UserLookups);
        Assert.Equal(0, scenario.UserIndependentLibraryLookups);
        Assert.Equal(0, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(1, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task OlderSourceReadCannotOverwriteANewerGetObservation()
    {
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = [640],
            Metadata = MetadataAvailability.MultipleWidths,
            RequestPositionTicks = 30_000L * TimeSpan.TicksPerMillisecond,
            SourceVideoWidth = 321,
        };
        SourceReadPlan older = scenario.QueueBlockedSourceRead();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        Task<HttpResponseMessage> olderProbe = fixture.HeadAsync();
        await older.Started.WaitAsync(TimeSpan.FromSeconds(10));

        scenario.SourceVideoWidth = 640;
        using HttpResponseMessage currentGet = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.OK, currentGet.StatusCode);
        Assert.Equal("1", currentGet.Headers.GetValues("X-Trickplay-Frame-Index").Single());

        older.Release();
        using HttpResponseMessage olderResponse = await olderProbe;
        await AssertTrickplayFrameProbeSuccessAsync(olderResponse, 3);

        using HttpResponseMessage retainedNewerSource = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(retainedNewerSource, 1);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(1, scenario.UserScopedSourceEnumerations);
    }

    [Fact]
    public async Task ExpiredGetSourceReadCannotGainFreshAgeAtCompletion()
    {
        var scenario = new PreviewScenario();
        SourceReadPlan slow = scenario.QueueBlockedSourceRead();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        Task<HttpResponseMessage> slowGet = fixture.GetAsync();
        await slow.Started.WaitAsync(TimeSpan.FromSeconds(10));
        scenario.Time.Advance(TimeSpan.FromMinutes(30));
        slow.Release();

        using HttpResponseMessage expired = await slowGet;
        Assert.Equal(HttpStatusCode.InternalServerError, expired.StatusCode);
        Assert.Equal(0, scenario.MetadataReadCount);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);

        using HttpResponseMessage refreshed = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshed, 0);
        Assert.Equal(2, scenario.UserIndependentLibraryLookups);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);
    }

    [Fact]
    public async Task GetRechecksMembershipWithoutReplacingTheWarmProbePositiveWithUserFilteredAbsence()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initialProbe = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(initialProbe, 0);

        scenario.Membership = SourceMembership.NotMember;
        using HttpResponseMessage currentGet = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.NotFound, currentGet.StatusCode);
        Assert.Equal(1, scenario.UserScopedSourceEnumerations);
        Assert.Equal(1, scenario.MetadataReadCount);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);

        using HttpResponseMessage retainedProbe = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(retainedProbe, 0);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);
    }

    [Fact]
    public async Task ProbeObservesRelinkingOnlyAfterThePositiveSourceLifetime()
    {
        var scenario = new PreviewScenario();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(initial, 0);

        scenario.Membership = SourceMembership.NotMember;
        scenario.Time.Advance(TimeSpan.FromMinutes(29));
        using HttpResponseMessage retainedPositive = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(retainedPositive, 0);

        scenario.Time.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage observedAbsence = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(observedAbsence, HttpStatusCode.NotFound);
        Assert.Equal(2, scenario.UserIndependentSourceEnumerations);

        scenario.Membership = SourceMembership.Member;
        scenario.Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        using HttpResponseMessage retainedAbsence = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(retainedAbsence, HttpStatusCode.NotFound);
        Assert.Equal(2, scenario.UserIndependentSourceEnumerations);

        scenario.Time.Advance(TimeSpan.FromSeconds(1));
        using HttpResponseMessage observedRelink = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(observedRelink, 0);
        Assert.Equal(3, scenario.UserIndependentSourceEnumerations);
    }

    [Fact]
    public async Task ProbeObservesSourceDeletionOnlyAfterThePositiveSourceLifetime()
    {
        var scenario = new PreviewScenario { UsesAlternateSource = true };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(initial, 0);

        scenario.SelectedVideo = ItemAvailability.Missing;
        using HttpResponseMessage currentGet = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.NotFound, currentGet.StatusCode);

        using HttpResponseMessage retainedPositive = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(retainedPositive, 0);

        scenario.Time.Advance(TimeSpan.FromMinutes(30));
        using HttpResponseMessage observedDeletion = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(observedDeletion, HttpStatusCode.NotFound);
        Assert.Equal(2, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(4, scenario.UserIndependentLibraryLookups);
    }

    [Fact]
    public async Task CurrentGetWidthReplacesTheWarmProbeWidth()
    {
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = [640],
            Metadata = MetadataAvailability.MultipleWidths,
            RequestPositionTicks = 30_000L * TimeSpan.TicksPerMillisecond,
            SourceVideoWidth = 321,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage oldProbe = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(oldProbe, 3);

        scenario.SourceVideoWidth = 640;
        using HttpResponseMessage currentGet = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.OK, currentGet.StatusCode);
        Assert.Equal("1", currentGet.Headers.GetValues("X-Trickplay-Frame-Index").Single());

        using HttpResponseMessage currentProbe = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(currentProbe, 1);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(1, scenario.UserScopedSourceEnumerations);
    }

    [Fact]
    public async Task CanceledSourceReadDoesNotWarmTheProbe()
    {
        var scenario = new PreviewScenario();
        SourceReadPlan pending = scenario.QueueBlockedSourceRead();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using var cancellation = new CancellationTokenSource();

        Task<HttpResponseMessage> canceledProbe = fixture.HeadAsync(cancellation.Token);
        await pending.Started.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceledProbe);
        pending.Release();

        using HttpResponseMessage refreshed = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshed, 0);
        Assert.Equal(2, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(1, scenario.MetadataReadCount);
    }

    [Fact]
    public async Task SwallowedProviderCancellationDoesNotCacheDynamicSourceAbsence()
    {
        var scenario = new PreviewScenario
        {
            HostSource = HostSourceKind.EligibleDynamic,
            UsesAlternateSource = true,
            Membership = SourceMembership.NotMember,
        };
        SourceReadPlan pending = scenario.QueueBlockedSourceRead();
        pending.SwallowsCancellation = true;
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using var cancellation = new CancellationTokenSource();

        Task<HttpResponseMessage> canceledProbe = fixture.HeadAsync(cancellation.Token);
        await pending.Started.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        try
        {
            using HttpResponseMessage ignored = await canceledProbe;
        }
        catch (OperationCanceledException)
        {
            // The assertion concerns the next caller, independently of transport cancellation timing.
        }

        await scenario.FirstRequestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        scenario.Membership = SourceMembership.Member;
        using HttpResponseMessage current = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(current, 0);
        Assert.Equal(2, scenario.UserIndependentSourceEnumerations);
        Assert.Equal(1, scenario.MetadataReadCount);
    }

    [Theory]
    [InlineData(SourceMembership.Member, SourceMembership.NotMember, HttpStatusCode.NotFound)]
    [InlineData(SourceMembership.NotMember, SourceMembership.Member, HttpStatusCode.OK)]
    public async Task NewerSourceObservationWinsAcrossPositiveAndAbsence(
        SourceMembership olderMembership,
        SourceMembership newerMembership,
        HttpStatusCode expectedCurrentStatus)
    {
        var scenario = new PreviewScenario { Membership = olderMembership };
        SourceReadPlan older = scenario.QueueBlockedSourceRead();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        Task<HttpResponseMessage> olderProbe = fixture.HeadAsync();
        await older.Started.WaitAsync(TimeSpan.FromSeconds(10));

        scenario.Membership = newerMembership;
        using HttpResponseMessage newerProbe = await fixture.HeadAsync();
        Assert.Equal(expectedCurrentStatus, newerProbe.StatusCode);

        older.Release();
        using HttpResponseMessage olderResponse = await olderProbe;
        Assert.Equal(
            olderMembership == SourceMembership.Member ? HttpStatusCode.OK : HttpStatusCode.NotFound,
            olderResponse.StatusCode);

        using HttpResponseMessage retainedNewer = await fixture.HeadAsync();
        Assert.Equal(expectedCurrentStatus, retainedNewer.StatusCode);
        Assert.Equal(2, scenario.UserIndependentSourceEnumerations);
    }

    [Fact]
    public async Task ExpiredNewerSourceAbsencePreventsOlderPositivePublication()
    {
        var scenario = new PreviewScenario();
        SourceReadPlan older = scenario.QueueBlockedSourceRead();
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        Task<HttpResponseMessage> olderProbe = fixture.HeadAsync();
        await older.Started.WaitAsync(TimeSpan.FromSeconds(10));
        scenario.Membership = SourceMembership.NotMember;
        SourceReadPlan newer = scenario.QueueBlockedSourceRead();
        Task<HttpResponseMessage> newerProbe = fixture.HeadAsync();
        await newer.Started.WaitAsync(TimeSpan.FromSeconds(10));
        scenario.Time.Advance(TimeSpan.FromMinutes(5));
        newer.Release();
        using HttpResponseMessage expired = await newerProbe;
        Assert.Equal(HttpStatusCode.InternalServerError, expired.StatusCode);

        older.Release();
        using HttpResponseMessage olderResponse = await olderProbe;
        await AssertTrickplayFrameProbeSuccessAsync(olderResponse, 0);

        using HttpResponseMessage current = await fixture.HeadAsync();
        Assert.Equal(HttpStatusCode.NotFound, current.StatusCode);
        Assert.Equal(3, scenario.UserIndependentSourceEnumerations);
    }

    [Fact]
    public async Task InvalidWidthRemainsAnErrorInsteadOfBecomingSourceAbsence()
    {
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = [640],
            Metadata = MetadataAvailability.ExactWidthMissing,
            SourceVideoWidth = 1,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(initial, HttpStatusCode.InternalServerError);

        scenario.SourceVideoWidth = 640;
        using HttpResponseMessage retainedInvalidWidth = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(
            retainedInvalidWidth,
            HttpStatusCode.InternalServerError);
        Assert.Equal(1, scenario.UserIndependentSourceEnumerations);

        scenario.Time.Advance(TimeSpan.FromMinutes(30));
        using HttpResponseMessage refreshed = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshed, 0);
        Assert.Equal(2, scenario.UserIndependentSourceEnumerations);
    }

    [Fact]
    public async Task ExpiredSourcePositiveIsNotServedWhileAnotherReadIsInProgress()
    {
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = [640],
            Metadata = MetadataAvailability.MultipleWidths,
            RequestPositionTicks = 30_000L * TimeSpan.TicksPerMillisecond,
            SourceVideoWidth = 321,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(initial, 3);

        scenario.Time.Advance(TimeSpan.FromMinutes(29));
        scenario.SourceVideoWidth = 640;
        SourceReadPlan pending = scenario.QueueBlockedSourceRead();
        Task<HttpResponseMessage> pendingGet = fixture.GetAsync();
        await pending.Started.WaitAsync(TimeSpan.FromSeconds(10));

        scenario.Time.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage refreshed = await fixture.HeadAsync();
        await AssertTrickplayFrameProbeSuccessAsync(refreshed, 1);
        Assert.Equal(2, scenario.UserIndependentSourceEnumerations);

        pending.Release();
        using HttpResponseMessage completedGet = await pendingGet;
        Assert.Equal(HttpStatusCode.OK, completedGet.StatusCode);
    }

    [Fact]
    public async Task ReusesWholeSourceAbsenceForFiveMinutesAfterCurrentGetAuthorization()
    {
        var scenario = new PreviewScenario
        {
            Metadata = MetadataAvailability.GeneratedMetadataMissing,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage initial = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(initial, HttpStatusCode.NotFound);
        Assert.Equal(1, scenario.MetadataReadCount);

        scenario.Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        using HttpResponseMessage sustainedHit = await fixture.HeadAsync();
        await AssertBodylessTrickplayFrameProbeFailureAsync(sustainedHit, HttpStatusCode.NotFound);
        Assert.Equal(1, scenario.MetadataReadCount);

        fixture.SetPlaybackAccess(false);
        using HttpResponseMessage forbidden = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(1, scenario.MetadataReadCount);

        fixture.SetPlaybackAccess(true);
        using HttpResponseMessage cachedAbsence = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.NotFound, cachedAbsence.StatusCode);
        Assert.Equal(1, scenario.MetadataReadCount);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);

        scenario.Metadata = MetadataAvailability.Available;
        scenario.Time.Advance(TimeSpan.FromSeconds(1));
        using HttpResponseMessage refreshed = await fixture.GetAsync();
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(2, scenario.MetadataReadCount);
    }

}
