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

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed class PreviewScenario
{
    private int metadataReadCount;
    private readonly Queue<MetadataReadPlan> metadataReadPlans = [];
    private readonly Queue<SourceReadPlan> sourceReadPlans = [];
    private int sourceSpritePathRequests;
    private int userIndependentLibraryLookups;
    private int userIndependentSourceEnumerations;
    private int userLookups;
    private int userScopedLibraryLookups;
    private int userScopedSourceEnumerations;

    public AuthenticationState Authentication { get; init; } = AuthenticationState.UserSession;

    public bool BlocksCacheAccessUntilCancellation { get; init; }

    public TaskCompletionSource CacheAccessStarted { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource FirstRequestCompleted { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public int[]? ConfiguredWidthResolutions { get; init; } = [320];

    public bool DeniesDefaultAuthorizationPolicy { get; init; }

    public bool DeniesLogicalVideoPlayback { get; init; }

    public bool DeniesSelectedVideoPlayback { get; init; }

    public bool FailsCacheAccess { get; init; }

    public HostSourceKind HostSource { get; init; } = HostSourceKind.Default;

    public List<Guid> LibraryLookupIds { get; } = [];

    public Guid LogicalItemId { get; init; } = ItemId;

    public string LogicalTitle { get; init; } = "Component logical video";

    public ItemAvailability LogicalVideo { get; set; } = ItemAvailability.Available;

    public string MediaSourceIdFormat { get; init; } = "D";

    public SourceMembership Membership { get; set; } = SourceMembership.Member;

    public MetadataAvailability Metadata { get; set; } = MetadataAvailability.Available;

    public int MetadataIntervalMilliseconds { get; init; } = 10_000;

    public int MetadataReadCount => Volatile.Read(ref metadataReadCount);

    public bool MutatesConfiguredTargetsDuringMetadataRead { get; init; }

    public long RequestPositionTicks { get; set; }

    public bool ReturnsMismatchedSourceIdentity { get; init; }

    public ItemAvailability SelectedVideo { get; set; } = ItemAvailability.Available;

    public string SelectedMediaPath { get; init; } = "/media/component-selected-source.mkv";

    public Guid SelectedSourceId => UsesAlternateSource ? AlternateSourceId : LogicalItemId;

    public string SelectedTitle { get; init; } = "Component selected source video";

    public SourceSpriteAvailability SourceSprite { get; init; } = SourceSpriteAvailability.Available;

    public int SourceSpritePathRequests => Volatile.Read(ref sourceSpritePathRequests);

    public ManualTimeProvider Time { get; } = new(
        new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero));

    public int? SourceVideoWidth { get; set; }

    public Guid UserId { get; init; } = PreviewHttpTestValues.UserId;

    public bool UsesAlternateSource { get; set; }

    public int UserIndependentLibraryLookups => Volatile.Read(ref userIndependentLibraryLookups);

    public int UserIndependentSourceEnumerations => Volatile.Read(ref userIndependentSourceEnumerations);

    public int UserLookups => Volatile.Read(ref userLookups);

    public int UserScopedLibraryLookups => Volatile.Read(ref userScopedLibraryLookups);

    public int UserScopedSourceEnumerations => Volatile.Read(ref userScopedSourceEnumerations);

    public void RecordSourceSpritePathRequest()
    {
        Interlocked.Increment(ref sourceSpritePathRequests);
    }

    public void RecordMetadataRead()
    {
        Interlocked.Increment(ref metadataReadCount);
    }

    public MetadataReadPlan QueueMetadataRead(MetadataAvailability availability)
    {
        var plan = new MetadataReadPlan(availability, MetadataReadOutcome.Succeeded);
        plan.Release();
        return QueueMetadataPlan(plan);
    }

    public MetadataReadPlan QueueBlockedMetadataRead(MetadataAvailability availability)
    {
        return QueueMetadataPlan(new MetadataReadPlan(availability, MetadataReadOutcome.Succeeded));
    }

    public void QueueMetadataFailure()
    {
        var plan = new MetadataReadPlan(MetadataAvailability.Available, MetadataReadOutcome.Failed);
        plan.Release();
        QueueMetadataPlan(plan);
    }

    public MetadataReadPlan QueueBlockedMetadataFailure()
    {
        return QueueMetadataPlan(
            new MetadataReadPlan(MetadataAvailability.Available, MetadataReadOutcome.Failed));
    }

    private MetadataReadPlan QueueMetadataPlan(MetadataReadPlan plan)
    {
        lock (metadataReadPlans)
        {
            metadataReadPlans.Enqueue(plan);
        }

        return plan;
    }

    public MetadataReadPlan? BeginMetadataRead()
    {
        lock (metadataReadPlans)
        {
            return metadataReadPlans.Count > 0 ? metadataReadPlans.Dequeue() : null;
        }
    }

    public SourceReadPlan? BeginSourceRead()
    {
        lock (sourceReadPlans)
        {
            return sourceReadPlans.Count > 0 ? sourceReadPlans.Dequeue() : null;
        }
    }

    public SourceReadPlan QueueBlockedSourceRead()
    {
        var plan = new SourceReadPlan(Membership, SourceVideoWidth);
        lock (sourceReadPlans)
        {
            sourceReadPlans.Enqueue(plan);
        }

        return plan;
    }

    public void RecordUserIndependentLibraryLookup()
    {
        Interlocked.Increment(ref userIndependentLibraryLookups);
    }

    public void RecordUserIndependentSourceEnumeration()
    {
        Interlocked.Increment(ref userIndependentSourceEnumerations);
    }

    public void RecordUserLookup()
    {
        Interlocked.Increment(ref userLookups);
    }

    public void RecordUserScopedLibraryLookup()
    {
        Interlocked.Increment(ref userScopedLibraryLookups);
    }

    public void RecordUserScopedSourceEnumeration()
    {
        Interlocked.Increment(ref userScopedSourceEnumerations);
    }
}
