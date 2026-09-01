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
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayPreviewHttpSpecs
{
    private const string ExpectedDefaultFrameToken = "f0000000000";
    private const string ExpectedDefaultSourceStamp = "d5b827fd3d17075e86151d7299ff22cd";
    private const string ExpectedDefaultEntityTag =
        $"\"{ExpectedDefaultSourceStamp}-{ExpectedDefaultFrameToken}\"";

    private static readonly Guid alternateSourceId = Guid.Parse("9fe0dc1f-c780-483e-86c8-fc16267127f6");
    private static readonly Guid itemId = Guid.Parse("3f728b7b-4aa5-4f65-b488-a6029edb6725");
    private static readonly Guid otherItemId = Guid.Parse("86bfb88a-2931-4454-8d5d-15a8c427f235");
    private static readonly Guid otherUserId = Guid.Parse("136fd48e-1dd2-4bee-a56f-44bf4ab0a377");
    private static readonly Guid unavailableSourceId = Guid.Parse("59036707-aa98-4b65-8875-d63c9d110906");
    private static readonly Guid userId = Guid.Parse("e07c89e3-a67e-49f5-9cbf-76b980ebe59a");

    [Fact]
    public async Task ServesGeneratedDefaultSourcePreview()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.Equal(ExpectedDefaultEntityTag, response.Headers.ETag?.Tag);
        Assert.Equal("MISS", response.Headers.GetValues("X-Trickplay-Cache").Single());
        Assert.False(response.Headers.Contains("X-Trickplay-Cache-File"));
        Assert.Contains("lookup;dur=", response.Headers.GetValues("Server-Timing").Single(), StringComparison.Ordinal);
        Assert.Contains("cache;dur=", response.Headers.GetValues("Server-Timing").Single(), StringComparison.Ordinal);
        Assert.Contains("decode;dur=", response.Headers.GetValues("Server-Timing").Single(), StringComparison.Ordinal);
        Assert.Contains("encode;dur=", response.Headers.GetValues("Server-Timing").Single(), StringComparison.Ordinal);

        byte[] content = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
        Assert.Equal(content.Length, response.Content.Headers.ContentLength);
        using SKBitmap decoded = SKBitmap.Decode(content);
        Assert.Equal(320, decoded.Width);
        Assert.Equal(180, decoded.Height);
        SKColor center = decoded.GetPixel(160, 90);
        Assert.InRange(center.Red, 240, 255);
        Assert.InRange(center.Green, 0, 15);
        Assert.InRange(center.Blue, 0, 15);
        string expectedEntryPath = GetDefaultEntryPath(fixture);
        Assert.True(File.Exists(expectedEntryPath));
        Assert.Single(Directory.EnumerateFiles(fixture.CacheRoot, "*.jpg", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(fixture.CacheRoot, "*.tmp", SearchOption.AllDirectories));

        Assert.Same(
            fixture.Services.GetRequiredService<ITrickplayPreview>(),
            fixture.Services.GetRequiredService<ITrickplayPreview>());
        Assert.Same(
            fixture.Services.GetRequiredService<IPreviewSourceResolver>(),
            fixture.Services.GetRequiredService<IPreviewSourceResolver>());
        Assert.Same(
            fixture.Services.GetRequiredService<IPreviewCache>(),
            fixture.Services.GetRequiredService<IPreviewCache>());
        Assert.Same(
            fixture.Services.GetRequiredService<ITrickplayPreviewEncoder>(),
            fixture.Services.GetRequiredService<ITrickplayPreviewEncoder>());
    }

    [Fact]
    public async Task ServesBufferedExistingPreviewCacheEntry()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();
        byte[] expectedContent = [0xFF, 0xD8, 1, 2, 3, 0xFF, 0xD9];
        string entryPath = GetDefaultEntryPath(fixture);
        Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
        await File.WriteAllBytesAsync(entryPath, expectedContent, CancellationToken.None);

        using HttpResponseMessage cachedResponse = await fixture.GetAsync();
        await File.WriteAllBytesAsync(entryPath, [9, 9, 9], CancellationToken.None);
        byte[] cachedContent = await cachedResponse.Content.ReadAsByteArrayAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.Equal(expectedContent, cachedContent);
        Assert.Equal(expectedContent.Length, cachedResponse.Content.Headers.ContentLength);
        Assert.Equal("image/jpeg", cachedResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", cachedResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(ExpectedDefaultEntityTag, cachedResponse.Headers.ETag?.Tag);
        Assert.True(cachedResponse.Headers.CacheControl?.Private);
        Assert.True(cachedResponse.Headers.CacheControl?.NoCache);
        Assert.Equal("HIT", cachedResponse.Headers.GetValues("X-Trickplay-Cache").Single());
        Assert.False(cachedResponse.Headers.Contains("X-Trickplay-Cache-File"));
        string serverTiming = cachedResponse.Headers.GetValues("Server-Timing").Single();
        Assert.Contains("lookup;dur=", serverTiming, StringComparison.Ordinal);
        Assert.Contains("cache;dur=", serverTiming, StringComparison.Ordinal);
        Assert.DoesNotContain("decode;dur=", serverTiming, StringComparison.Ordinal);
        Assert.DoesNotContain("encode;dur=", serverTiming, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Cache.CallCount);
    }

    [Fact]
    public async Task ReturnsNotModifiedAfterRevalidatingCurrentSource()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();
        using HttpResponseMessage generatedResponse = await fixture.GetAsync();
        string entityTag = Assert.IsType<string>(generatedResponse.Headers.ETag?.Tag);

        using HttpResponseMessage response = await fixture.GetConditionalAsync(entityTag);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
        Assert.Equal(entityTag, response.Headers.ETag?.Tag);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.False(response.Headers.Contains("X-Trickplay-Cache"));
        Assert.False(response.Headers.Contains("X-Trickplay-Cache-File"));
        string serverTiming = response.Headers.GetValues("Server-Timing").Single();
        Assert.Contains("lookup;dur=", serverTiming, StringComparison.Ordinal);
        Assert.DoesNotContain("cache;dur=", serverTiming, StringComparison.Ordinal);
        Assert.DoesNotContain("decode;dur=", serverTiming, StringComparison.Ordinal);
        Assert.DoesNotContain("encode;dur=", serverTiming, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Cache.CallCount);
    }

    [Theory]
    [InlineData(ConditionalEntityTagKind.Weak)]
    [InlineData(ConditionalEntityTagKind.Wildcard)]
    public async Task UsesWeakComparisonAndWildcardForConditionalRequests(ConditionalEntityTagKind conditionKind)
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();
        using HttpResponseMessage generatedResponse = await fixture.GetAsync();
        string entityTag = Assert.IsType<string>(generatedResponse.Headers.ETag?.Tag);
        string condition = conditionKind switch
        {
            ConditionalEntityTagKind.Weak => string.Concat("W/", entityTag),
            ConditionalEntityTagKind.Wildcard => "*",
            _ => throw new ArgumentOutOfRangeException(nameof(conditionKind), conditionKind, "Unknown condition kind."),
        };

        using HttpResponseMessage response = await fixture.GetConditionalAsync(condition);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Equal(1, fixture.Cache.CallCount);
    }

    [Fact]
    public async Task IgnoresStaleConditionalEntityTagAfterSourceSnapshotChanges()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();
        using HttpResponseMessage originalResponse = await fixture.GetAsync();
        string originalEntityTag = Assert.IsType<string>(originalResponse.Headers.ETag?.Tag);
        DateTime changedWriteTime = File.GetLastWriteTimeUtc(fixture.SourceSpritePath).AddSeconds(1);
        File.SetLastWriteTimeUtc(fixture.SourceSpritePath, changedWriteTime);

        using HttpResponseMessage response = await fixture.GetConditionalAsync(originalEntityTag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(originalEntityTag, response.Headers.ETag?.Tag);
        Assert.Equal("MISS", response.Headers.GetValues("X-Trickplay-Cache").Single());
        Assert.Equal(2, fixture.Cache.CallCount);
        Assert.Equal(2, Directory.EnumerateFiles(fixture.CacheRoot, "*.jpg", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task ServesAuthorizedAlternateSourcePreview()
    {
        var scenario = new PreviewScenario { UsesAlternateSource = true };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Guid[] expectedLookups = [itemId, alternateSourceId];
        Assert.Equal(expectedLookups, scenario.LibraryLookupIds);
        Assert.Single(fixture.Cache.Identities);
        Assert.StartsWith(
            string.Concat(alternateSourceId.ToString("N"), Path.DirectorySeparatorChar),
            fixture.Cache.Identities[0].RelativePath,
            StringComparison.Ordinal);
    }

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
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
    }

    [Theory]
    [InlineData(ForbiddenCondition.LogicalVideoPlaybackDenied)]
    [InlineData(ForbiddenCondition.SelectedVideoPlaybackDenied)]
    public async Task ForbidsAuthenticatedPlaybackDenial(ForbiddenCondition condition)
    {
        PreviewScenario scenario = CreateForbiddenScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertAuthorizationErrorResponseAsync(response);
        Assert.Equal(0, fixture.Cache.CallCount);
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
    [InlineData(NotFoundCondition.SelectedVideoHidden)]
    [InlineData(NotFoundCondition.SelectedItemWrongType)]
    [InlineData(NotFoundCondition.ExactMetadataMissing)]
    [InlineData(NotFoundCondition.ThumbnailsMissing)]
    [InlineData(NotFoundCondition.ThumbnailsNegative)]
    [InlineData(NotFoundCondition.ManagerPathMissing)]
    [InlineData(NotFoundCondition.SourceSpriteMissing)]
    public async Task ConcealsUnavailableResource(NotFoundCondition condition)
    {
        PreviewScenario scenario = CreateNotFoundScenario(condition);
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemDetailsResponseAsync(response);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
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
    [InlineData("/TrickplayCropper/Videos/3f728b7b-4aa5-4f65-b488-a6029edb6725/Preview")]
    [InlineData("/TrickplayCropper/Videos/3f728b7b-4aa5-4f65-b488-a6029edb6725/Preview?PositionTicks=not-a-number")]
    [InlineData("/TrickplayCropper/Videos/3f728b7b-4aa5-4f65-b488-a6029edb6725/Preview?PositionTicks=9223372036854775808")]
    [InlineData("/TrickplayCropper/Videos/3f728b7b-4aa5-4f65-b488-a6029edb6725/Preview?PositionTicks=-1")]
    [InlineData(
        "/TrickplayCropper/Videos/3f728b7b-4aa5-4f65-b488-a6029edb6725/Preview"
        + "?MediaSourceId=not-a-guid&PositionTicks=0")]
    [InlineData("/TrickplayCropper/Videos/not-a-guid/Preview?PositionTicks=0")]
    public async Task RejectsMissingMalformedAndNegativeRequestValues(string requestPath)
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();

        using HttpResponseMessage response = await fixture.Client.GetAsync(requestPath, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetailsResponseAsync(response);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
    }

    [Theory]
    [InlineData(InternalFailureCondition.ContradictoryFrameWidth, "FrameWidthMatchesResolutionKey", 640)]
    [InlineData(InternalFailureCondition.FrameWidthZero, "FrameWidthPositive", 0)]
    [InlineData(InternalFailureCondition.FrameHeightZero, "FrameHeightPositive", 0)]
    [InlineData(InternalFailureCondition.IntervalZero, "IntervalMillisecondsPositive", 0)]
    [InlineData(InternalFailureCondition.TileWidthZero, "TileWidthPositive", 0)]
    [InlineData(InternalFailureCondition.TileHeightZero, "TileHeightPositive", 0)]
    [InlineData(InternalFailureCondition.CropXOverflow, "CropXInt32", 4_294_967_294L)]
    [InlineData(InternalFailureCondition.CropYOverflow, "CropYInt32", 4_294_967_294L)]
    [InlineData(InternalFailureCondition.CropRightOverflow, "CropRightInt32", 4_294_967_294L)]
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
        Assert.Equal(new EventId(1000, "TrickplayPreviewRequestFailed"), log.EventId);
        Assert.Null(log.Exception);
        Assert.Equal(itemId, log.Properties["ItemId"]);
        Assert.Equal(itemId, log.Properties["MediaSourceId"]);
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
            LogicalItemId = otherItemId,
            LogicalTitle = "A different logical title",
            MediaSourceIdFormat = "N",
            SelectedMediaPath = "/different/private/media/location.mkv",
            SelectedTitle = "A different selected title",
            UserId = otherUserId,
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

    private static string GetDefaultEntryPath(PreviewHostFixture fixture)
    {
        return Path.Combine(
            fixture.CacheRoot,
            itemId.ToString("N"),
            "w0320",
            $"s000000-{ExpectedDefaultSourceStamp}",
            $"{ExpectedDefaultFrameToken}.jpg");
    }

    private static PreviewScenario CreateForbiddenScenario(ForbiddenCondition condition)
    {
        return condition switch
        {
            ForbiddenCondition.LogicalVideoPlaybackDenied => new PreviewScenario
            {
                DeniesLogicalVideoPlayback = true,
            },
            ForbiddenCondition.SelectedVideoPlaybackDenied => new PreviewScenario
            {
                DeniesSelectedVideoPlayback = true,
                UsesAlternateSource = true,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown forbidden condition."),
        };
    }

    private static PreviewScenario CreateNotFoundScenario(NotFoundCondition condition)
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
            NotFoundCondition.SelectedVideoHidden => CreateSelectedAvailabilityScenario(ItemAvailability.Hidden),
            NotFoundCondition.SelectedItemWrongType => CreateSelectedAvailabilityScenario(ItemAvailability.WrongType),
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

    private static PreviewScenario CreateInternalFailureScenario(InternalFailureCondition condition)
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
                RequestPositionTicks = 2 * TimeSpan.TicksPerMillisecond,
            },
            InternalFailureCondition.CropYOverflow => new PreviewScenario
            {
                Metadata = MetadataAvailability.CropYOverflow,
                RequestPositionTicks = 2 * TimeSpan.TicksPerMillisecond,
            },
            InternalFailureCondition.CropRightOverflow => new PreviewScenario
            {
                Metadata = MetadataAvailability.CropRightOverflow,
                RequestPositionTicks = TimeSpan.TicksPerMillisecond,
            },
            InternalFailureCondition.CropBottomOverflow => new PreviewScenario
            {
                Metadata = MetadataAvailability.CropBottomOverflow,
                RequestPositionTicks = TimeSpan.TicksPerMillisecond,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown failure condition."),
        };
    }

    private static void AssertAvailableSelectionDiagnostics(
        InternalFailureCondition condition,
        RecordedLog log)
    {
        switch (condition)
        {
            case InternalFailureCondition.ContradictoryFrameWidth:
                Assert.Equal(0, log.Properties["FrameIndex"]);
                Assert.Equal(0, log.Properties["SpriteIndex"]);
                Assert.Equal(0, log.Properties["Row"]);
                Assert.Equal(0, log.Properties["Column"]);
                Assert.Equal(0L, log.Properties["CropX"]);
                Assert.Equal(0L, log.Properties["CropY"]);
                Assert.Equal(640, log.Properties["CropWidth"]);
                Assert.Equal(180, log.Properties["CropHeight"]);
                break;
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
                Assert.Equal(2, log.Properties["FrameIndex"]);
                Assert.Equal(0, log.Properties["SpriteIndex"]);
                Assert.Equal(0, log.Properties["Row"]);
                Assert.Equal(2, log.Properties["Column"]);
                Assert.Equal(4_294_967_294L, log.Properties["CropX"]);
                Assert.Equal(0L, log.Properties["CropY"]);
                Assert.Equal(int.MaxValue, log.Properties["CropWidth"]);
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
                Assert.Equal(1, log.Properties["FrameIndex"]);
                Assert.Equal(0, log.Properties["SpriteIndex"]);
                Assert.Equal(0, log.Properties["Row"]);
                Assert.Equal(1, log.Properties["Column"]);
                Assert.Equal((long)int.MaxValue, log.Properties["CropX"]);
                Assert.Equal(0L, log.Properties["CropY"]);
                Assert.Equal(int.MaxValue, log.Properties["CropWidth"]);
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

    private static TrickplayMetadata CreateExpectedMetadata(InternalFailureCondition condition)
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
                int.MaxValue,
                180,
                1,
                3,
                1,
                3),
            InternalFailureCondition.CropYOverflow => new TrickplayMetadata(
                320,
                int.MaxValue,
                1,
                1,
                3,
                3),
            InternalFailureCondition.CropRightOverflow => new TrickplayMetadata(
                int.MaxValue,
                180,
                1,
                2,
                1,
                2),
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

    private static PreviewScenario CreateLogicalAvailabilityScenario(ItemAvailability availability)
    {
        return new PreviewScenario { LogicalVideo = availability };
    }

    private static PreviewScenario CreateSelectedAvailabilityScenario(ItemAvailability availability)
    {
        return new PreviewScenario
        {
            SelectedVideo = availability,
            UsesAlternateSource = true,
        };
    }

    private static async Task AssertAuthorizationErrorResponseAsync(HttpResponseMessage response)
    {
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
        Assert.False(response.Headers.Contains("X-Trickplay-Cache"));
    }

    private static async Task AssertProblemDetailsResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await using Stream content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using JsonDocument problem = await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
        Assert.Equal((int)response.StatusCode, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("title", out JsonElement title));
        Assert.False(string.IsNullOrWhiteSpace(title.GetString()));
        Assert.False(response.Headers.Contains("X-Trickplay-Cache"));
    }

    private sealed class PreviewHostFixture : IAsyncDisposable
    {
        private readonly IHost host;
        private readonly string temporaryDirectory;

        private PreviewHostFixture(IHost host, string temporaryDirectory, string sourceSpritePath)
        {
            this.host = host;
            this.temporaryDirectory = temporaryDirectory;
            SourceSpritePath = sourceSpritePath;
            Client = host.GetTestClient();
        }

        public RecordingPreviewCache Cache => Services.GetRequiredService<RecordingPreviewCache>();

        public string CacheRoot => Path.Combine(
            temporaryDirectory,
            "Jellyfin.Plugin.TrickplayCropper",
            "preview-v1");

        public HttpClient Client { get; }

        public int ErrorLogCount => ErrorLogs.Length;

        public RecordedLog[] ErrorLogs => Services.GetRequiredService<RecordingLogger<TrickplayPreview>>().Errors;

        public IServiceProvider Services => host.Services;

        public string SourceSpritePath { get; }

        public static Task<PreviewHostFixture> CreateAsync()
        {
            return CreateAsync(new PreviewScenario());
        }

        public static async Task<PreviewHostFixture> CreateAsync(PreviewScenario scenario)
        {
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"trickplay-preview-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);
            string sourceSpritePath = CreateSourceSprite(temporaryDirectory, scenario);
            var context = new PreviewHostContext(temporaryDirectory, sourceSpritePath, scenario);

            var hostBuilder = new HostBuilder();
            hostBuilder.ConfigureWebHost(webHost => ConfigureWebHost(webHost, context));
            IHost host = await hostBuilder.StartAsync(CancellationToken.None);
            return new PreviewHostFixture(host, temporaryDirectory, sourceSpritePath);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync();
            host.Dispose();
            Directory.Delete(temporaryDirectory, recursive: true);
        }

        public Task<HttpResponseMessage> GetAsync()
        {
            return SendAsync(null);
        }

        public Task<HttpResponseMessage> GetConditionalAsync(string ifNoneMatch)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ifNoneMatch);
            return SendAsync(ifNoneMatch);
        }

        public string[] EnumerateCacheFiles()
        {
            return Directory.Exists(CacheRoot)
                ? Directory.EnumerateFiles(CacheRoot, "*", SearchOption.AllDirectories).ToArray()
                : [];
        }

        public void SetPlaybackAccess(bool hasPlaybackAccess)
        {
            SetPlaybackPermission(Services.GetRequiredService<User>(), hasPlaybackAccess);
        }

        private async Task<HttpResponseMessage> SendAsync(string? ifNoneMatch)
        {
            PreviewScenario scenario = Services.GetRequiredService<PreviewScenario>();
            string mediaSourceQuery = scenario.UsesAlternateSource
                ? $"MediaSourceId={alternateSourceId.ToString(scenario.MediaSourceIdFormat)}&"
                : string.Empty;
            string requestPath = string.Create(
                CultureInfo.InvariantCulture,
                $"/TrickplayCropper/Videos/{scenario.LogicalItemId:D}/Preview?{mediaSourceQuery}"
                + $"PositionTicks={scenario.RequestPositionTicks}");
            using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
            if (ifNoneMatch is not null)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            }

            return await Client.SendAsync(request, CancellationToken.None);
        }

        private static void ConfigureWebHost(IWebHostBuilder webHost, PreviewHostContext context)
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services => ConfigureServices(services, context));
            webHost.Configure(application =>
            {
                application.UseRouting();
                application.UseAuthentication();
                application.UseAuthorization();
                application.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        }

        private static void ConfigureServices(IServiceCollection services, PreviewHostContext context)
        {
            services.AddLogging();
            services.AddSingleton(context.Scenario);
            ConfigureAuthenticationServices(services);
            services.AddControllers().AddApplicationPart(typeof(TrickplayPreviewController).Assembly);

            IApplicationPaths applicationPaths = CreateApplicationPaths(context.TemporaryDirectory);
            RegisterJellyfinFakes(services, context, applicationPaths);
            RegisterPluginServices(services, applicationPaths, context);
        }

        private static void ConfigureAuthenticationServices(IServiceCollection services)
        {
            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthenticationHandler.SchemeName)
                    .RequireAuthenticatedUser()
                    .AddRequirements(new TestDefaultAuthorizationRequirement())
                    .Build();
            });
            services.AddSingleton<IAuthorizationHandler, TestDefaultAuthorizationHandler>();
        }

        private static void RegisterJellyfinFakes(
            IServiceCollection services,
            PreviewHostContext context,
            IApplicationPaths applicationPaths)
        {
            User user = CreateUser(context.Scenario);
            services.AddSingleton(user);
            services.AddSingleton(CreateUserManager(user));
            services.AddSingleton(CreateLibraryManager(context.Scenario, user));
            services.AddSingleton(CreateMediaSourceManager(context.Scenario, user));
            services.AddSingleton(CreateTrickplayManager(context));
            services.AddSingleton(applicationPaths);
        }

        private static void RegisterPluginServices(
            IServiceCollection services,
            IApplicationPaths applicationPaths,
            PreviewHostContext context)
        {
            var registrator = new PluginServiceRegistrator();
            registrator.RegisterServices(services, InterfaceMock.Create<IServerApplicationHost>().Service);

            var recordingLogger = new RecordingLogger<TrickplayPreview>();
            services.AddSingleton(recordingLogger);
            services.AddSingleton<ILogger<TrickplayPreview>>(recordingLogger);
            string cacheRoot = Path.Combine(
                context.TemporaryDirectory,
                "Jellyfin.Plugin.TrickplayCropper",
                "preview-v1");
            string? failureMessage = context.Scenario.FailsCacheAccess
                ? $"component-secret SourceSpritePath={context.SourceSpritePath} CachePath={cacheRoot}"
                : null;
            var cache = new RecordingPreviewCache(
                new DiskPreviewCache(applicationPaths, TimeProvider.System),
                failureMessage);
            services.AddSingleton(cache);
            services.AddSingleton<IPreviewCache>(cache);
        }

        private static User CreateUser(PreviewScenario scenario)
        {
            var user = new User("component-user", "test-provider", "test-reset-provider")
            {
                Id = scenario.UserId,
            };
            SetPlaybackPermission(user, !scenario.DeniesLogicalVideoPlayback);
            return user;
        }

        private static IUserManager CreateUserManager(User user)
        {
            InterfaceMockSpecs<IUserManager> mock = InterfaceMock.Create<IUserManager>();
            mock.Handle("GetUserById", arguments =>
                Equals(arguments?[0], user.Id) ? user : null);
            return mock.Service;
        }

        private static ILibraryManager CreateLibraryManager(PreviewScenario scenario, User user)
        {
            var logicalVideo = new Video
            {
                Id = scenario.LogicalItemId,
                Name = scenario.LogicalTitle,
            };
            var selectedVideo = new Video
            {
                Id = scenario.SelectedSourceId,
                Name = scenario.SelectedTitle,
                Path = scenario.SelectedMediaPath,
            };
            var context = new VideoLookupContext
            {
                LogicalVideo = logicalVideo,
                Scenario = scenario,
                SelectedVideo = selectedVideo,
                User = user,
            };

            InterfaceMockSpecs<ILibraryManager> mock = InterfaceMock.Create<ILibraryManager>();
            mock.Handle("GetItemById", arguments => ResolveVideoLookup(arguments, context));
            mock.Handle("GetLibraryOptions", arguments =>
                ReferenceEquals(arguments?[0], context.SelectedVideo)
                    ? new LibraryOptions { SaveTrickplayWithMedia = false }
                    : throw new InvalidOperationException("Library options were requested for the wrong video."));
            return mock.Service;
        }

        private static Video? ResolveVideoLookup(object?[]? arguments, VideoLookupContext context)
        {
            if (arguments?.Length != 2
                || !ReferenceEquals(arguments[1], context.User)
                || arguments[0] is not Guid requestedId)
            {
                throw new InvalidOperationException("The video lookup did not use the current user-scoped overload.");
            }

            context.Scenario.LibraryLookupIds.Add(requestedId);
            if (context.Scenario.LibraryLookupIds.Count == 1)
            {
                return context.Scenario.LogicalVideo == ItemAvailability.Available ? context.LogicalVideo : null;
            }

            if (requestedId != context.Scenario.SelectedSourceId)
            {
                return null;
            }

            if (context.Scenario.DeniesSelectedVideoPlayback)
            {
                SetPlaybackPermission(context.User, false);
            }

            return context.Scenario.SelectedVideo == ItemAvailability.Available ? context.SelectedVideo : null;
        }

        private static IMediaSourceManager CreateMediaSourceManager(PreviewScenario scenario, User user)
        {
            InterfaceMockSpecs<IMediaSourceManager> mock = InterfaceMock.Create<IMediaSourceManager>();
            mock.Handle("GetPlaybackMediaSources", arguments =>
            {
                if (arguments?.Length != 5
                    || arguments[0] is not Video video
                    || video.Id != scenario.LogicalItemId
                    || !ReferenceEquals(arguments[1], user))
                {
                    throw new InvalidOperationException("Playback sources were not enumerated for the current user.");
                }

                string memberId = scenario.Membership switch
                {
                    SourceMembership.Member => scenario.SelectedSourceId.ToString("D"),
                    SourceMembership.NotMember => unavailableSourceId.ToString("D"),
                    SourceMembership.Malformed => "not-a-guid",
                    _ => throw new InvalidOperationException("Unknown source-membership scenario."),
                };
                IReadOnlyList<MediaSourceInfo> mediaSources = [new MediaSourceInfo { Id = memberId }];
                return Task.FromResult(mediaSources);
            });
            return mock.Service;
        }

        private static ITrickplayManager CreateTrickplayManager(PreviewHostContext context)
        {
            TrickplayInfo metadata = CreateMetadata(context.Scenario);
            Dictionary<int, TrickplayInfo> resolutions = context.Scenario.Metadata == MetadataAvailability.ExactWidthMissing
                ? new Dictionary<int, TrickplayInfo> { [640] = metadata }
                : new Dictionary<int, TrickplayInfo> { [320] = metadata };

            InterfaceMockSpecs<ITrickplayManager> mock = InterfaceMock.Create<ITrickplayManager>();
            mock.Handle("GetTrickplayResolutions", arguments =>
                Equals(arguments?[0], context.Scenario.SelectedSourceId)
                    ? Task.FromResult(resolutions)
                    : Task.FromResult(new Dictionary<int, TrickplayInfo>()));
            mock.Handle("GetTrickplayTilePathAsync", arguments => ResolveSourceSpritePath(arguments, context));
            return mock.Service;
        }

        private static TrickplayInfo CreateMetadata(PreviewScenario scenario)
        {
            var metadata = new TrickplayInfo
            {
                ItemId = scenario.SelectedSourceId,
                Width = 320,
                Height = 180,
                TileWidth = 2,
                TileHeight = 2,
                ThumbnailCount = 4,
                Interval = 10_000,
            };
            switch (scenario.Metadata)
            {
                case MetadataAvailability.Available:
                    {
                        break;
                    }

                case MetadataAvailability.ContradictoryFrameWidth:
                case MetadataAvailability.ExactWidthMissing:
                    {
                        metadata.Width = 640;
                        break;
                    }

                case MetadataAvailability.CropXOverflow:
                    {
                        metadata.Width = int.MaxValue;
                        metadata.TileWidth = 3;
                        metadata.TileHeight = 1;
                        metadata.ThumbnailCount = 3;
                        metadata.Interval = 1;
                        break;
                    }

                case MetadataAvailability.CropYOverflow:
                    {
                        metadata.Height = int.MaxValue;
                        metadata.TileWidth = 1;
                        metadata.TileHeight = 3;
                        metadata.ThumbnailCount = 3;
                        metadata.Interval = 1;
                        break;
                    }

                case MetadataAvailability.CropRightOverflow:
                    {
                        metadata.Width = int.MaxValue;
                        metadata.TileWidth = 2;
                        metadata.TileHeight = 1;
                        metadata.ThumbnailCount = 2;
                        metadata.Interval = 1;
                        break;
                    }

                case MetadataAvailability.CropBottomOverflow:
                    {
                        metadata.Height = int.MaxValue;
                        metadata.TileWidth = 1;
                        metadata.TileHeight = 2;
                        metadata.ThumbnailCount = 2;
                        metadata.Interval = 1;
                        break;
                    }

                case MetadataAvailability.FrameHeightZero:
                    {
                        metadata.Height = 0;
                        break;
                    }

                case MetadataAvailability.FrameWidthZero:
                    {
                        metadata.Width = 0;
                        break;
                    }

                case MetadataAvailability.IntervalZero:
                    {
                        metadata.Interval = 0;
                        break;
                    }

                case MetadataAvailability.NegativeThumbnails:
                    {
                        metadata.ThumbnailCount = -1;
                        break;
                    }

                case MetadataAvailability.NoThumbnails:
                    {
                        metadata.ThumbnailCount = 0;
                        break;
                    }

                case MetadataAvailability.TileHeightZero:
                    {
                        metadata.TileHeight = 0;
                        break;
                    }

                case MetadataAvailability.TileWidthZero:
                    {
                        metadata.TileWidth = 0;
                        break;
                    }

                default:
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(scenario),
                            scenario.Metadata,
                            "Unknown metadata scenario.");
                    }
            }

            return metadata;
        }

        private static Task<string> ResolveSourceSpritePath(object?[]? arguments, PreviewHostContext context)
        {
            if (!Equals(arguments?[1], 320)
                || !Equals(arguments?[2], 0)
                || !Equals(arguments?[3], false))
            {
                throw new InvalidOperationException("The Source Sprite path request was not normalized.");
            }

            string path = context.Scenario.SourceSprite switch
            {
                SourceSpriteAvailability.Available => context.SourceSpritePath,
                SourceSpriteAvailability.DimensionMismatch => context.SourceSpritePath,
                SourceSpriteAvailability.ManagerFailure => throw new IOException(
                    $"manager-secret SourceSpritePath={context.SourceSpritePath}"),
                SourceSpriteAvailability.ManagerPathMissing => string.Empty,
                SourceSpriteAvailability.FileMissing => Path.Combine(
                    context.TemporaryDirectory,
                    "missing-source-sprite.jpg"),
                _ => throw new InvalidOperationException("Unknown Source Sprite scenario."),
            };
            return Task.FromResult(path);
        }

        private static IApplicationPaths CreateApplicationPaths(string temporaryDirectory)
        {
            InterfaceMockSpecs<IApplicationPaths> mock = InterfaceMock.Create<IApplicationPaths>();
            mock.Handle("get_TempDirectory", _ => temporaryDirectory);
            return mock.Service;
        }

        private static string CreateSourceSprite(string temporaryDirectory, PreviewScenario scenario)
        {
            string sourceSpritePath = Path.Combine(temporaryDirectory, "source-sprite.jpg");
            int sourceWidth = scenario.SourceSprite == SourceSpriteAvailability.DimensionMismatch ? 320 : 640;
            using var bitmap = new SKBitmap(sourceWidth, 360, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var canvas = new SKCanvas(bitmap);
            DrawCell(canvas, SKColors.Red, 0, 0);
            DrawCell(canvas, SKColors.Green, 320, 0);
            DrawCell(canvas, SKColors.Blue, 0, 180);
            DrawCell(canvas, SKColors.Yellow, 320, 180);
            using FileStream output = File.Create(sourceSpritePath);
            Assert.True(bitmap.Encode(output, SKEncodedImageFormat.Jpeg, quality: 100));
            output.Close();
            File.SetLastWriteTimeUtc(sourceSpritePath, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            return sourceSpritePath;
        }

        private static void DrawCell(SKCanvas canvas, SKColor color, int left, int top)
        {
            using var paint = new SKPaint { Color = color };
            canvas.DrawRect(left, top, 320, 180, paint);
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private const string IsApiKeyClaim = "Jellyfin-IsApiKey";
        private const string UserIdClaim = "Jellyfin-UserId";

        public const string SchemeName = "ComponentTest";

        private readonly PreviewScenario scenario;

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            PreviewScenario scenario)
            : base(options, logger, encoder)
        {
            this.scenario = scenario;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            AuthenticateResult result = scenario.Authentication switch
            {
                AuthenticationState.UserSession => CreateUserSessionResult(scenario.UserId),
                AuthenticationState.ApiKeyWithoutCurrentUser => CreateApiKeyResult(),
                AuthenticationState.Missing => AuthenticateResult.NoResult(),
                AuthenticationState.Invalid => AuthenticateResult.Fail("The component-test session is invalid."),
                AuthenticationState.UnusableUserSession => AuthenticateResult.Fail(
                    "The component-test session is no longer usable."),
                _ => throw new InvalidOperationException("Unknown authentication scenario."),
            };
            return Task.FromResult(result);
        }

        private static AuthenticateResult CreateUserSessionResult(Guid authenticatedUserId)
        {
            Claim[] claims =
            [
                new Claim(UserIdClaim, authenticatedUserId.ToString("N")),
                new Claim(IsApiKeyClaim, bool.FalseString),
            ];
            return CreateAuthenticatedResult(claims);
        }

        private static AuthenticateResult CreateApiKeyResult()
        {
            Claim[] claims =
            [
                new Claim(UserIdClaim, Guid.Empty.ToString("N")),
                new Claim(IsApiKeyClaim, bool.TrueString),
            ];
            return CreateAuthenticatedResult(claims);
        }

        private static AuthenticateResult CreateAuthenticatedResult(Claim[] claims)
        {
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return AuthenticateResult.Success(ticket);
        }
    }

    private sealed class TestDefaultAuthorizationHandler
        : AuthorizationHandler<TestDefaultAuthorizationRequirement>
    {
        private readonly PreviewScenario scenario;

        public TestDefaultAuthorizationHandler(PreviewScenario scenario)
        {
            this.scenario = scenario;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            TestDefaultAuthorizationRequirement requirement)
        {
            if (scenario.DeniesDefaultAuthorizationPolicy)
            {
                context.Fail();
            }
            else
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestDefaultAuthorizationRequirement : IAuthorizationRequirement;

    private sealed class RecordingPreviewCache : IPreviewCache
    {
        private readonly string? failureMessage;
        private readonly List<PreviewIdentity> identities = [];
        private readonly IPreviewCache inner;
        private int callCount;

        public RecordingPreviewCache(IPreviewCache inner, string? failureMessage)
        {
            this.inner = inner;
            this.failureMessage = failureMessage;
        }

        public int CallCount => Volatile.Read(ref callCount);

        public PreviewIdentity[] Identities
        {
            get
            {
                lock (identities)
                {
                    return identities.ToArray();
                }
            }
        }

        async Task<PreviewCacheResult> IPreviewCache.GetOrCreateAsync(
            PreviewIdentity identity,
            Func<Stream, CancellationToken, Task<PreviewEncodingTelemetry>> writer,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            lock (identities)
            {
                identities.Add(identity);
            }

            if (failureMessage is not null)
            {
                throw new InvalidOperationException(failureMessage);
            }

            return await inner.GetOrCreateAsync(identity, writer, cancellationToken).ConfigureAwait(false);
        }

        Task IPreviewCache.ClearAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return inner.ClearAsync(progress, cancellationToken);
        }
    }

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        private readonly List<RecordedLog> errors = [];

        public RecordedLog[] Errors
        {
            get
            {
                lock (errors)
                {
                    return errors.ToArray();
                }
            }
        }

        IDisposable? ILogger.BeginScope<TState>(TState state)
        {
            return null;
        }

        bool ILogger.IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        void ILogger.Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                IReadOnlyDictionary<string, object?> properties = state
                    is IEnumerable<KeyValuePair<string, object?>> structuredState
                    ? structuredState.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    : new Dictionary<string, object?>(StringComparer.Ordinal);
                var log = new RecordedLog(eventId, formatter(state, exception), properties, exception);
                lock (errors)
                {
                    errors.Add(log);
                }
            }
        }
    }

    private sealed record RecordedLog(
        EventId EventId,
        string Message,
        IReadOnlyDictionary<string, object?> Properties,
        Exception? Exception);

    public class InterfaceMockSpecs<TInterface> : DispatchProxy
        where TInterface : class
    {
        private readonly Dictionary<string, Func<object?[]?, object?>> handlers = new(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="InterfaceMockSpecs{TInterface}"/> class.
        /// </summary>
        public InterfaceMockSpecs()
        {
        }

        public TInterface Service => (TInterface)(object)this;

        public void Handle(string methodName, Func<object?[]?, object?> handler)
        {
            handlers.Add(methodName, handler);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            if (handlers.TryGetValue(targetMethod.Name, out Func<object?[]?, object?>? handler))
            {
                return handler(args);
            }

            throw new InvalidOperationException($"Unexpected Jellyfin call: {targetMethod.Name}.");
        }
    }

    private static class InterfaceMock
    {
        public static InterfaceMockSpecs<TInterface> Create<TInterface>()
            where TInterface : class
        {
            TInterface service = DispatchProxy.Create<TInterface, InterfaceMockSpecs<TInterface>>();
            return (InterfaceMockSpecs<TInterface>)(object)service;
        }
    }

    private sealed record PreviewHostContext(
        string TemporaryDirectory,
        string SourceSpritePath,
        PreviewScenario Scenario);

    private sealed class VideoLookupContext
    {
        public required Video LogicalVideo { get; init; }

        public required PreviewScenario Scenario { get; init; }

        public required Video SelectedVideo { get; init; }

        public required User User { get; init; }
    }

    private sealed class PreviewScenario
    {
        public AuthenticationState Authentication { get; init; } = AuthenticationState.UserSession;

        public bool DeniesDefaultAuthorizationPolicy { get; init; }

        public bool DeniesLogicalVideoPlayback { get; init; }

        public bool DeniesSelectedVideoPlayback { get; init; }

        public bool FailsCacheAccess { get; init; }

        public List<Guid> LibraryLookupIds { get; } = [];

        public Guid LogicalItemId { get; init; } = itemId;

        public string LogicalTitle { get; init; } = "Component logical video";

        public ItemAvailability LogicalVideo { get; init; } = ItemAvailability.Available;

        public string MediaSourceIdFormat { get; init; } = "D";

        public SourceMembership Membership { get; init; } = SourceMembership.Member;

        public MetadataAvailability Metadata { get; init; } = MetadataAvailability.Available;

        public long RequestPositionTicks { get; init; }

        public ItemAvailability SelectedVideo { get; init; } = ItemAvailability.Available;

        public string SelectedMediaPath { get; init; } = "/media/component-selected-source.mkv";

        public Guid SelectedSourceId => UsesAlternateSource ? alternateSourceId : LogicalItemId;

        public string SelectedTitle { get; init; } = "Component selected source video";

        public SourceSpriteAvailability SourceSprite { get; init; } = SourceSpriteAvailability.Available;

        public Guid UserId { get; init; } = userId;

        public bool UsesAlternateSource { get; init; }
    }

    public enum AuthenticationState
    {
        UserSession,
        ApiKeyWithoutCurrentUser,
        Missing,
        Invalid,
        UnusableUserSession,
    }

    public enum ConditionalEntityTagKind
    {
        Weak,
        Wildcard,
    }

    public enum ForbiddenCondition
    {
        LogicalVideoPlaybackDenied,
        SelectedVideoPlaybackDenied,
    }

    private enum ItemAvailability
    {
        Available,
        Missing,
        Hidden,
        WrongType,
    }

    private enum MetadataAvailability
    {
        Available,
        ContradictoryFrameWidth,
        CropBottomOverflow,
        CropRightOverflow,
        CropXOverflow,
        CropYOverflow,
        ExactWidthMissing,
        FrameHeightZero,
        FrameWidthZero,
        IntervalZero,
        NegativeThumbnails,
        NoThumbnails,
        TileHeightZero,
        TileWidthZero,
    }

    public enum InternalFailureCondition
    {
        ContradictoryFrameWidth,
        FrameWidthZero,
        FrameHeightZero,
        IntervalZero,
        TileWidthZero,
        TileHeightZero,
        CropXOverflow,
        CropYOverflow,
        CropRightOverflow,
        CropBottomOverflow,
    }

    public enum NotFoundCondition
    {
        LogicalVideoMissing,
        LogicalVideoHidden,
        LogicalItemWrongType,
        SelectedSourceNotMember,
        SelectedSourceMembershipMalformed,
        SelectedVideoMissing,
        SelectedVideoHidden,
        SelectedItemWrongType,
        ExactMetadataMissing,
        ThumbnailsMissing,
        ThumbnailsNegative,
        ManagerPathMissing,
        SourceSpriteMissing,
    }

    private enum SourceMembership
    {
        Member,
        NotMember,
        Malformed,
    }

    private enum SourceSpriteAvailability
    {
        Available,
        DimensionMismatch,
        ManagerFailure,
        ManagerPathMissing,
        FileMissing,
    }

    private static void SetPlaybackPermission(User user, bool hasPlaybackAccess)
    {
        user.Permissions.Clear();
        user.Permissions.Add(new Permission(PermissionKind.EnableMediaPlayback, hasPlaybackAccess));
    }
}
