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

internal static class TrickplayPreviewHttpSupport
{
    internal const string ExpectedDefaultFrameToken = "f0000000000";
    internal const string ExpectedDefaultSourceStamp = "d5b827fd3d17075e86151d7299ff22cd";
    internal const string ExpectedDefaultEntityTag =
        $"\"{ExpectedDefaultSourceStamp}-{ExpectedDefaultFrameToken}\"";

    public static TheoryData<string> MalformedPreviewRequestPaths => new()
    {
        { PreviewPath },
        { $"{PreviewPath}?PositionTicks=not-a-number" },
        { $"{PreviewPath}?PositionTicks=9223372036854775808" },
        { $"{PreviewPath}?PositionTicks=-1" },
        { $"{PreviewPath}?MediaSourceId=not-a-guid&PositionTicks=0" },
        { "/TrickplayCropper/Videos/not-a-guid/Preview?PositionTicks=0" },
    };

    internal static string PreviewPath => string.Create(
        CultureInfo.InvariantCulture,
        $"/TrickplayCropper/Videos/{ItemId:D}/Preview");

    internal static PreviewScenario CreateKestrelScenario(TrickplayFrameProbeKestrelCondition condition)
    {
        return condition switch
        {
            TrickplayFrameProbeKestrelCondition.Success => new PreviewScenario(),
            TrickplayFrameProbeKestrelCondition.MalformedInput => new PreviewScenario(),
            TrickplayFrameProbeKestrelCondition.UnauthenticatedSession => new PreviewScenario
            {
                Authentication = AuthenticationState.Missing,
            },
            TrickplayFrameProbeKestrelCondition.DefaultPolicyDenied => new PreviewScenario
            {
                DeniesDefaultAuthorizationPolicy = true,
            },
            TrickplayFrameProbeKestrelCondition.ApiKeyWithoutCurrentUser => new PreviewScenario
            {
                Authentication = AuthenticationState.ApiKeyWithoutCurrentUser,
            },
            TrickplayFrameProbeKestrelCondition.ConcealedResource => new PreviewScenario
            {
                LogicalVideo = ItemAvailability.Missing,
            },
            TrickplayFrameProbeKestrelCondition.InvalidMetadata => new PreviewScenario
            {
                Metadata = MetadataAvailability.ContradictoryFrameWidth,
            },
            _ => throw new ArgumentOutOfRangeException(
                nameof(condition),
                condition,
                "Unknown real-Kestrel Trickplay Frame Probe condition."),
        };
    }

    internal static string GetDefaultEntryPath(PreviewHostFixture fixture)
    {
        return Path.Combine(
            fixture.CacheRoot,
            ItemId.ToString("N"),
            "w0320",
            $"s000000-{ExpectedDefaultSourceStamp}",
            $"{ExpectedDefaultFrameToken}.jpg");
    }

    internal static int[]? CreateInvalidConfiguration(ConfigurationFailureKind kind)
    {
        return kind switch
        {
            ConfigurationFailureKind.UnreadableSnapshot => null,
            ConfigurationFailureKind.NonPositiveConfiguredTarget => [320, 0],
            ConfigurationFailureKind.NonPositiveSelectedResolution => [1],
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown configuration failure kind."),
        };
    }

    internal static void AssertExpectedUnavailableDebugReason(RecordedLog[] debugLogs, NotFoundCondition condition)
    {
        RecordedLog[] unavailable = debugLogs
            .Where(entry => entry.EventId.Id == 1001)
            .ToArray();
        string? expectedReason = ExpectedDebugReason(condition);
        if (expectedReason is null)
        {
            Assert.Empty(unavailable);
            return;
        }

        RecordedLog log = Assert.Single(unavailable);
        Assert.Equal(LogLevel.Debug, log.Level);
        Assert.Equal("TrickplayPreviewUnavailable", log.EventId.Name);
        Assert.Equal(expectedReason, log.Properties["Reason"]!.ToString());
        using System.Text.Json.JsonDocument envelope = System.Text.Json.JsonDocument.Parse(
            log.Message["TrickplayDebug ".Length..]);
        Assert.Equal(1001, envelope.RootElement.GetProperty("EventId").GetInt32());
        Assert.Equal(expectedReason, envelope.RootElement.GetProperty("Reason").GetString());
    }

    internal static string? ExpectedDebugReason(NotFoundCondition condition)
    {
        return condition switch
        {
            NotFoundCondition.NoConfiguredTarget => "NoConfiguredTarget",
            NotFoundCondition.GeneratedMetadataMissing => "NoGeneratedMetadata",
            NotFoundCondition.ExactMetadataMissing => "SelectedResolutionMissing",
            NotFoundCondition.ThumbnailsMissing => "NoThumbnails",
            NotFoundCondition.ThumbnailsNegative => "NoThumbnails",
            NotFoundCondition.ManagerPathMissing => "SourceSpriteUnavailable",
            NotFoundCondition.SourceSpriteMissing => "SourceSpriteUnavailable",
            _ => null,
        };
    }

    internal static PreviewScenario CreateNotFoundScenario(NotFoundCondition condition)
    {
        return condition switch
        {
            NotFoundCondition.LogicalVideoMissing => CreateLogicalAvailabilityScenario(ItemAvailability.Missing),
            NotFoundCondition.LogicalVideoHidden => CreateLogicalAvailabilityScenario(ItemAvailability.Hidden),
            NotFoundCondition.LogicalItemWrongType => CreateLogicalAvailabilityScenario(ItemAvailability.WrongType),
            NotFoundCondition.SelectedSourceNotMember => new PreviewScenario
            {
                Membership = SourceMembership.NotMember,
                UsesAlternateSource = true,
            },
            NotFoundCondition.SelectedSourceMembershipMalformed => new PreviewScenario
            {
                Membership = SourceMembership.Malformed,
                UsesAlternateSource = true,
            },
            NotFoundCondition.SelectedVideoMissing => CreateSelectedAvailabilityScenario(ItemAvailability.Missing),
            NotFoundCondition.SelectedVideoIdentityMismatch => new PreviewScenario
            {
                ReturnsMismatchedSourceIdentity = true,
                UsesAlternateSource = true,
            },
            NotFoundCondition.SelectedVideoHidden => CreateSelectedAvailabilityScenario(ItemAvailability.Hidden),
            NotFoundCondition.SelectedItemWrongType => CreateSelectedAvailabilityScenario(ItemAvailability.WrongType),
            NotFoundCondition.NoConfiguredTarget => new PreviewScenario
            {
                ConfiguredWidthResolutions = [],
            },
            NotFoundCondition.GeneratedMetadataMissing => new PreviewScenario
            {
                Metadata = MetadataAvailability.GeneratedMetadataMissing,
            },
            NotFoundCondition.ExactMetadataMissing => new PreviewScenario
            {
                Metadata = MetadataAvailability.ExactWidthMissing,
            },
            NotFoundCondition.ThumbnailsMissing => new PreviewScenario
            {
                Metadata = MetadataAvailability.NoThumbnails,
            },
            NotFoundCondition.ThumbnailsNegative => new PreviewScenario
            {
                Metadata = MetadataAvailability.NegativeThumbnails,
            },
            NotFoundCondition.ManagerPathMissing => new PreviewScenario
            {
                SourceSprite = SourceSpriteAvailability.ManagerPathMissing,
            },
            NotFoundCondition.SourceSpriteMissing => new PreviewScenario
            {
                SourceSprite = SourceSpriteAvailability.FileMissing,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown not-found condition."),
        };
    }

    internal static PreviewScenario CreateInternalFailureScenario(InternalFailureCondition condition)
    {
        return condition switch
        {
            InternalFailureCondition.ContradictoryFrameWidth => new PreviewScenario
            {
                Metadata = MetadataAvailability.ContradictoryFrameWidth,
            },
            InternalFailureCondition.FrameWidthZero => new PreviewScenario
            {
                Metadata = MetadataAvailability.FrameWidthZero,
            },
            InternalFailureCondition.FrameHeightZero => new PreviewScenario
            {
                Metadata = MetadataAvailability.FrameHeightZero,
            },
            InternalFailureCondition.IntervalZero => new PreviewScenario
            {
                Metadata = MetadataAvailability.IntervalZero,
            },
            InternalFailureCondition.TileWidthZero => new PreviewScenario
            {
                Metadata = MetadataAvailability.TileWidthZero,
            },
            InternalFailureCondition.TileHeightZero => new PreviewScenario
            {
                Metadata = MetadataAvailability.TileHeightZero,
            },
            InternalFailureCondition.CropXOverflow => new PreviewScenario
            {
                Metadata = MetadataAvailability.CropXOverflow,
                RequestPositionTicks = 7_000_000 * TimeSpan.TicksPerMillisecond,
            },
            InternalFailureCondition.CropYOverflow => new PreviewScenario
            {
                Metadata = MetadataAvailability.CropYOverflow,
                RequestPositionTicks = 2 * TimeSpan.TicksPerMillisecond,
            },
            InternalFailureCondition.CropRightOverflow => new PreviewScenario
            {
                Metadata = MetadataAvailability.CropRightOverflow,
                RequestPositionTicks = 6_710_886 * TimeSpan.TicksPerMillisecond,
            },
            InternalFailureCondition.CropBottomOverflow => new PreviewScenario
            {
                Metadata = MetadataAvailability.CropBottomOverflow,
                RequestPositionTicks = TimeSpan.TicksPerMillisecond,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown failure condition."),
        };
    }

    internal static void AssertAvailableSelectionDiagnostics(
        InternalFailureCondition condition,
        RecordedLog log)
    {
        switch (condition)
        {
            case InternalFailureCondition.ContradictoryFrameWidth:
            case InternalFailureCondition.FrameWidthZero:
            case InternalFailureCondition.FrameHeightZero:
            case InternalFailureCondition.IntervalZero:
            case InternalFailureCondition.TileWidthZero:
            case InternalFailureCondition.TileHeightZero:
                Assert.Null(log.Properties["FrameIndex"]);
                Assert.Null(log.Properties["SpriteIndex"]);
                Assert.Null(log.Properties["Row"]);
                Assert.Null(log.Properties["Column"]);
                Assert.Null(log.Properties["CropX"]);
                Assert.Null(log.Properties["CropY"]);
                Assert.Null(log.Properties["CropWidth"]);
                Assert.Null(log.Properties["CropHeight"]);
                break;
            case InternalFailureCondition.CropXOverflow:
                Assert.Equal(7_000_000, log.Properties["FrameIndex"]);
                Assert.Equal(0, log.Properties["SpriteIndex"]);
                Assert.Equal(0, log.Properties["Row"]);
                Assert.Equal(7_000_000, log.Properties["Column"]);
                Assert.Equal(2_240_000_000L, log.Properties["CropX"]);
                Assert.Equal(0L, log.Properties["CropY"]);
                Assert.Equal(320, log.Properties["CropWidth"]);
                Assert.Equal(180, log.Properties["CropHeight"]);
                break;
            case InternalFailureCondition.CropYOverflow:
                Assert.Equal(2, log.Properties["FrameIndex"]);
                Assert.Equal(0, log.Properties["SpriteIndex"]);
                Assert.Equal(2, log.Properties["Row"]);
                Assert.Equal(0, log.Properties["Column"]);
                Assert.Equal(0L, log.Properties["CropX"]);
                Assert.Equal(4_294_967_294L, log.Properties["CropY"]);
                Assert.Equal(320, log.Properties["CropWidth"]);
                Assert.Equal(int.MaxValue, log.Properties["CropHeight"]);
                break;
            case InternalFailureCondition.CropRightOverflow:
                Assert.Equal(6_710_886, log.Properties["FrameIndex"]);
                Assert.Equal(0, log.Properties["SpriteIndex"]);
                Assert.Equal(0, log.Properties["Row"]);
                Assert.Equal(6_710_886, log.Properties["Column"]);
                Assert.Equal(2_147_483_520L, log.Properties["CropX"]);
                Assert.Equal(0L, log.Properties["CropY"]);
                Assert.Equal(320, log.Properties["CropWidth"]);
                Assert.Equal(180, log.Properties["CropHeight"]);
                break;
            case InternalFailureCondition.CropBottomOverflow:
                Assert.Equal(1, log.Properties["FrameIndex"]);
                Assert.Equal(0, log.Properties["SpriteIndex"]);
                Assert.Equal(1, log.Properties["Row"]);
                Assert.Equal(0, log.Properties["Column"]);
                Assert.Equal(0L, log.Properties["CropX"]);
                Assert.Equal((long)int.MaxValue, log.Properties["CropY"]);
                Assert.Equal(320, log.Properties["CropWidth"]);
                Assert.Equal(int.MaxValue, log.Properties["CropHeight"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(condition),
                    condition,
                    "Unknown internal failure condition.");
        }
    }

    internal static TrickplayMetadata CreateExpectedMetadata(InternalFailureCondition condition)
    {
        return condition switch
        {
            InternalFailureCondition.ContradictoryFrameWidth => new TrickplayMetadata(640, 180, 10_000, 2, 2, 4),
            InternalFailureCondition.FrameWidthZero => new TrickplayMetadata(0, 180, 10_000, 2, 2, 4),
            InternalFailureCondition.FrameHeightZero => new TrickplayMetadata(320, 0, 10_000, 2, 2, 4),
            InternalFailureCondition.IntervalZero => new TrickplayMetadata(320, 180, 0, 2, 2, 4),
            InternalFailureCondition.TileWidthZero => new TrickplayMetadata(320, 180, 10_000, 0, 2, 4),
            InternalFailureCondition.TileHeightZero => new TrickplayMetadata(320, 180, 10_000, 2, 0, 4),
            InternalFailureCondition.CropXOverflow => new TrickplayMetadata(
                320,
                180,
                1,
                10_000_000,
                1,
                10_000_000),
            InternalFailureCondition.CropYOverflow => new TrickplayMetadata(
                320,
                int.MaxValue,
                1,
                1,
                3,
                3),
            InternalFailureCondition.CropRightOverflow => new TrickplayMetadata(
                320,
                180,
                1,
                10_000_000,
                1,
                10_000_000),
            InternalFailureCondition.CropBottomOverflow => new TrickplayMetadata(
                320,
                int.MaxValue,
                1,
                1,
                2,
                2),
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown failure condition."),
        };
    }

    internal static PreviewScenario CreateLogicalAvailabilityScenario(ItemAvailability availability)
    {
        return new PreviewScenario { LogicalVideo = availability };
    }

    internal static PreviewScenario CreateSelectedAvailabilityScenario(ItemAvailability availability)
    {
        return new PreviewScenario
        {
            SelectedVideo = availability,
            UsesAlternateSource = true,
        };
    }

    internal static async Task AssertAuthorizationErrorResponseAsync(HttpResponseMessage response)
    {
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
        Assert.False(response.Headers.Contains("X-Trickplay-Cache"));
        Assert.False(response.Headers.Contains("X-Trickplay-Frame-Index"));
    }

    internal static async Task AssertTrickplayFrameProbeSuccessAsync(HttpResponseMessage response, int expectedFrameIndex)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertBodylessWithoutGetOnlyHeadersAsync(response);
        Assert.Equal(
            expectedFrameIndex.ToString(CultureInfo.InvariantCulture),
            response.Headers.GetValues("X-Trickplay-Frame-Index").Single());
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
    }

    internal static async Task AssertBodylessTrickplayFrameProbeFailureAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        await AssertBodylessWithoutGetOnlyHeadersAsync(response);
        Assert.False(response.Headers.Contains("X-Trickplay-Frame-Index"));
    }

    internal static async Task AssertBodylessWithoutGetOnlyHeadersAsync(HttpResponseMessage response)
    {
        await AssertBodylessAsync(response);
        Assert.False(response.Headers.Contains("ETag"));
        Assert.False(response.Headers.Contains("Server-Timing"));
        Assert.False(response.Headers.Contains("X-Trickplay-Cache"));
    }

    internal static async Task AssertBodylessAsync(HttpResponseMessage response)
    {
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Null(response.Content.Headers.ContentLength);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
    }

    internal static async Task AssertProblemDetailsResponseAsync(HttpResponseMessage response)
    {
        Assert.False(response.Headers.Contains("X-Trickplay-Frame-Index"));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await using Stream content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using JsonDocument problem = await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
        Assert.Equal((int)response.StatusCode, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("title", out JsonElement title));
        Assert.False(string.IsNullOrWhiteSpace(title.GetString()));
        Assert.False(response.Headers.Contains("X-Trickplay-Cache"));
    }

}
