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

public sealed class TrickplayPreviewFailureHttpSpecs : TrickplayPreviewHttpSharedSpecs
{
    [Theory]
    [InlineData(InternalFailureCondition.ContradictoryFrameWidth, "FrameWidthMatchesResolutionKey", 640)]
    [InlineData(InternalFailureCondition.FrameWidthZero, "FrameWidthPositive", 0)]
    [InlineData(InternalFailureCondition.FrameHeightZero, "FrameHeightPositive", 0)]
    [InlineData(InternalFailureCondition.IntervalZero, "IntervalMillisecondsPositive", 0)]
    [InlineData(InternalFailureCondition.TileWidthZero, "TileWidthPositive", 0)]
    [InlineData(InternalFailureCondition.TileHeightZero, "TileHeightPositive", 0)]
    [InlineData(InternalFailureCondition.CropXOverflow, "CropXInt32", 2_240_000_000L)]
    [InlineData(InternalFailureCondition.CropYOverflow, "CropYInt32", 4_294_967_294L)]
    [InlineData(InternalFailureCondition.CropRightOverflow, "CropRightInt32", 2_147_483_840L)]
    [InlineData(InternalFailureCondition.CropBottomOverflow, "CropBottomInt32", 4_294_967_294L)]
    public async Task ReportsInvalidMetadataAndCheckedArithmeticAsInternalErrors(
        InternalFailureCondition condition,
        string failedValidation,
        long failedValue)
    {
        PreviewScenario scenario = CreateInternalFailureScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertProblemDetailsResponseAsync(response);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        RecordedLog log = Assert.Single(fixture.ErrorLogs);
        TrickplayMetadata expectedMetadata = CreateExpectedMetadata(condition);
        Assert.Equal(expectedMetadata.FrameWidth, log.Properties["FrameWidth"]);
        Assert.Equal(expectedMetadata.FrameHeight, log.Properties["FrameHeight"]);
        Assert.Equal(expectedMetadata.IntervalMilliseconds, log.Properties["IntervalMilliseconds"]);
        Assert.Equal(expectedMetadata.TileWidth, log.Properties["TileWidth"]);
        Assert.Equal(expectedMetadata.TileHeight, log.Properties["TileHeight"]);
        Assert.Equal(expectedMetadata.ThumbnailCount, log.Properties["ThumbnailCount"]);
        Assert.Equal(nameof(InvalidTrickplayMetadataException), log.Properties["ExceptionType"]);
        Assert.Equal(failedValidation, log.Properties["FailedValidation"]);
        Assert.Equal(failedValue, log.Properties["FailedValue"]);
        AssertAvailableSelectionDiagnostics(condition, log);
        Assert.Empty(fixture.EnumerateCacheFiles());
    }

    [Theory]
    [InlineData(ConfigurationFailureKind.UnreadableSnapshot, "ConfigurationReadable", null, null)]
    [InlineData(ConfigurationFailureKind.NonPositiveConfiguredTarget, "ConfiguredTargetPositive", 0L, "320,0")]
    [InlineData(ConfigurationFailureKind.NonPositiveSelectedResolution, "SelectedResolutionPositive", 0L, "1")]
    public async Task ReportsInvalidConfigurationAsInternalError(
        ConfigurationFailureKind kind,
        string failedValidation,
        long? failedValue,
        string? expectedConfiguredTargets)
    {
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = CreateInvalidConfiguration(kind),
            SourceVideoWidth = 640,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertProblemDetailsResponseAsync(response);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        RecordedLog log = Assert.Single(fixture.ErrorLogs);
        Assert.Equal(nameof(InvalidTrickplayConfigurationException), log.Properties["ExceptionType"]);
        Assert.Equal(failedValidation, log.Properties["FailedValidation"]);
        Assert.Equal(failedValue, log.Properties["FailedValue"]);
        Assert.Equal(640, log.Properties["NormalizationSourceWidth"]);
        if (expectedConfiguredTargets is null)
        {
            Assert.Null(log.Properties["ConfiguredTargets"]);
        }
        else
        {
            Assert.Equal(expectedConfiguredTargets, log.Properties["ConfiguredTargets"]);
        }

        Assert.Null(log.Properties["ChosenTarget"]);
        Assert.Null(log.Properties["SelectedResolution"]);
        Assert.Null(log.Properties["GeneratedKeys"]);
        Assert.DoesNotContain(fixture.SourceSpritePath, log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.CacheRoot, log.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.EnumerateCacheFiles());
    }

    [Fact]
    public async Task IncludesConfigurationAndGeneratedKeysInTheMetadataFailureDiagnostic()
    {
        var scenario = new PreviewScenario
        {
            Metadata = MetadataAvailability.ContradictoryFrameWidth,
            SourceVideoWidth = 640,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        RecordedLog log = Assert.Single(fixture.ErrorLogs);
        Assert.Equal(nameof(InvalidTrickplayMetadataException), log.Properties["ExceptionType"]);
        Assert.Equal("320", log.Properties["ConfiguredTargets"]);
        Assert.Equal(320, log.Properties["ChosenTarget"]);
        Assert.Equal(320, log.Properties["SelectedResolution"]);
        Assert.Equal(640, log.Properties["NormalizationSourceWidth"]);
        Assert.Equal("320", log.Properties["GeneratedKeys"]);
    }

    [Fact]
    public async Task UsesTheCopiedConfigurationSnapshotWhenTheHostArrayChangesDuringMetadataRead()
    {
        int[] configuredTargets = [320];
        var scenario = new PreviewScenario
        {
            ConfiguredWidthResolutions = configuredTargets,
            Metadata = MetadataAvailability.ContradictoryFrameWidth,
            MutatesConfiguredTargetsDuringMetadataRead = true,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(640, configuredTargets[0]);
        RecordedLog log = Assert.Single(fixture.ErrorLogs);
        Assert.Equal("320", log.Properties["ConfiguredTargets"]);
        Assert.Equal(320, log.Properties["ChosenTarget"]);
        Assert.Equal(320, log.Properties["SelectedResolution"]);
    }

    [Fact]
    public async Task RecordsFrameSelectionAndCacheDispositionDebugEventsForAServedGet()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        RecordedLog frameSelected = Assert.Single(
            fixture.DebugLogs,
            entry => entry.EventId.Id == 1002);
        Assert.Equal("TrickplayPreviewFrameSelected", frameSelected.EventId.Name);
        Assert.Equal(0, frameSelected.Properties["FrameIndex"]);
        Assert.Equal(0, frameSelected.Properties["SpriteIndex"]);
        RecordedLog cacheDisposition = Assert.Single(
            fixture.DebugLogs,
            entry => entry.EventId.Id == 1003);
        Assert.Equal("TrickplayPreviewCacheDisposition", cacheDisposition.EventId.Name);
        Assert.Equal(nameof(PreviewCacheDisposition.Miss), cacheDisposition.Properties["CacheDisposition"]!.ToString());
    }

    [Fact]
    public async Task PreservesSelectionDiagnosticsWhenManagerPathResolutionFails()
    {
        long positionTicks = 3L * 10_000 * TimeSpan.TicksPerMillisecond;
        var scenario = new PreviewScenario
        {
            RequestPositionTicks = positionTicks,
            SourceSprite = SourceSpriteAvailability.ManagerFailure,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        RecordedLog log = Assert.Single(fixture.ErrorLogs);
        Assert.Equal(1, fixture.SourceSpritePathRequests);
        Assert.Equal(3, log.Properties["FrameIndex"]);
        Assert.Equal(0, log.Properties["SpriteIndex"]);
        Assert.Equal(1, log.Properties["Row"]);
        Assert.Equal(1, log.Properties["Column"]);
        Assert.Equal(320L, log.Properties["CropX"]);
        Assert.Equal(180L, log.Properties["CropY"]);
        Assert.Equal(nameof(IOException), log.Properties["ExceptionType"]);
        Assert.Null(log.Properties["SourceLength"]);
        Assert.DoesNotContain("manager-secret", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.SourceSpritePath, log.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogsActualDimensionsWhenGeometryDoesNotMatchMetadata()
    {
        var scenario = new PreviewScenario
        {
            RequestPositionTicks = 10_000L * TimeSpan.TicksPerMillisecond,
            SourceSprite = SourceSpriteAvailability.DimensionMismatch,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        RecordedLog log = Assert.Single(fixture.ErrorLogs);
        Assert.Equal(1, fixture.SourceSpritePathRequests);
        Assert.Equal(320, log.Properties["ActualWidth"]);
        Assert.Equal(360, log.Properties["ActualHeight"]);
        Assert.Equal("SUBSET", log.Properties["DecodePath"]);
        Assert.Null(log.Properties["SkiaResult"]);
        Assert.Equal("SourceSpriteDimensionsMatchMetadata", log.Properties["FailedValidation"]);
        Assert.Empty(fixture.EnumerateCacheFiles());
    }

    [Fact]
    public async Task LogsOneCompleteRedactedDiagnosticForAnInternalFailure()
    {
        long positionTicks = 3L * 10_000 * TimeSpan.TicksPerMillisecond;
        var scenario = new PreviewScenario
        {
            FailsCacheAccess = true,
            RequestPositionTicks = positionTicks,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertProblemDetailsResponseAsync(response);
        RecordedLog log = Assert.Single(fixture.ErrorLogs);
        Assert.Equal(1, fixture.SourceSpritePathRequests);
        Assert.Equal(new EventId(1000, "TrickplayPreviewRequestFailed"), log.EventId);
        Assert.Null(log.Exception);
        Assert.Equal(ItemId, log.Properties["ItemId"]);
        Assert.Equal(ItemId, log.Properties["MediaSourceId"]);
        Assert.Equal(positionTicks, log.Properties["PositionTicks"]);
        Assert.Equal(320, log.Properties["FrameWidth"]);
        Assert.Equal(180, log.Properties["FrameHeight"]);
        Assert.Equal(10_000, log.Properties["IntervalMilliseconds"]);
        Assert.Equal(2, log.Properties["TileWidth"]);
        Assert.Equal(2, log.Properties["TileHeight"]);
        Assert.Equal(4, log.Properties["ThumbnailCount"]);
        Assert.Equal(3, log.Properties["FrameIndex"]);
        Assert.Equal(0, log.Properties["SpriteIndex"]);
        Assert.Equal(1, log.Properties["Row"]);
        Assert.Equal(1, log.Properties["Column"]);
        Assert.Equal(320L, log.Properties["CropX"]);
        Assert.Equal(180L, log.Properties["CropY"]);
        Assert.Equal(320, log.Properties["CropWidth"]);
        Assert.Equal(180, log.Properties["CropHeight"]);
        Assert.Equal(new FileInfo(fixture.SourceSpritePath).Length, log.Properties["SourceLength"]);
        Assert.Equal(
            File.GetLastWriteTimeUtc(fixture.SourceSpritePath).Ticks,
            log.Properties["SourceLastWriteUtcTicks"]);
        Assert.Equal(nameof(InvalidOperationException), log.Properties["ExceptionType"]);
        Assert.DoesNotContain("component-secret", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.SourceSpritePath, log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.CacheRoot, log.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.EnumerateCacheFiles());
    }

    [Fact]
    public async Task ExcludesUserLogicalItemTitlesRawFormattingAndLocationsFromIdentity()
    {
        var baselineScenario = new PreviewScenario { UsesAlternateSource = true };
        var changedScenario = new PreviewScenario
        {
            LogicalItemId = OtherItemId,
            LogicalTitle = "A different logical title",
            MediaSourceIdFormat = "N",
            SelectedMediaPath = "/different/private/media/location.mkv",
            SelectedTitle = "A different selected title",
            UserId = OtherUserId,
            UsesAlternateSource = true,
        };
        await using PreviewHostFixture baseline = await PreviewHostFixture.CreateAsync(baselineScenario);
        await using PreviewHostFixture changed = await PreviewHostFixture.CreateAsync(changedScenario);

        using HttpResponseMessage baselineResponse = await baseline.GetAsync();
        using HttpResponseMessage changedResponse = await changed.GetAsync();

        Assert.Equal(HttpStatusCode.OK, baselineResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, changedResponse.StatusCode);
        Assert.NotEqual(baseline.SourceSpritePath, changed.SourceSpritePath);
        Assert.NotEqual(baseline.CacheRoot, changed.CacheRoot);
        Assert.Equal(Assert.Single(baseline.Cache.Identities), Assert.Single(changed.Cache.Identities));
    }

    [Fact]
    public async Task PropagatesRequestCancellationWithoutPublishingOrLoggingAnInternalError()
    {
        var scenario = new PreviewScenario { BlocksCacheAccessUntilCancellation = true };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using var cancellation = new CancellationTokenSource();

        Task<HttpResponseMessage> response = fixture.GetAsync(cancellation.Token);
        await scenario.CacheAccessStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
        Assert.Empty(fixture.EnumerateCacheFiles());
        Assert.Equal(0, fixture.ErrorLogCount);
    }

}
