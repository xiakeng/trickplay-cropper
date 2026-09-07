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

public sealed class TrickplayPreviewGetResponseHttpSpecs
{
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
        Assert.Equal("0", response.Headers.GetValues("X-Trickplay-Frame-Index").Single());
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
            fixture.Services.GetRequiredService<IPreviewContextResolver>(),
            fixture.Services.GetRequiredService<IPreviewContextResolver>());
        Assert.Same(
            fixture.Services.GetRequiredService<IPreviewSourceResolver>(),
            fixture.Services.GetRequiredService<IPreviewSourceResolver>());
        Assert.Same(
            fixture.Services.GetRequiredService<IPreviewCache>(),
            fixture.Services.GetRequiredService<IPreviewCache>());
        Assert.Same(
            fixture.Services.GetRequiredService<IPreviewCache>(),
            fixture.Services.GetRequiredService<IPreviewCacheMaintenance>());
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
        Assert.Equal("0", cachedResponse.Headers.GetValues("X-Trickplay-Frame-Index").Single());
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
        Assert.Equal("0", response.Headers.GetValues("X-Trickplay-Frame-Index").Single());
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
        Assert.Equal("0", response.Headers.GetValues("X-Trickplay-Frame-Index").Single());
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
        Assert.Equal("0", response.Headers.GetValues("X-Trickplay-Frame-Index").Single());
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
        Guid[] expectedLookups = [ItemId, AlternateSourceId];
        Assert.Equal(expectedLookups, scenario.LibraryLookupIds);
        Assert.Single(fixture.Cache.Identities);
        Assert.StartsWith(
            string.Concat(AlternateSourceId.ToString("N"), Path.DirectorySeparatorChar),
            fixture.Cache.Identities[0].RelativePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServesTheMinimumConfiguredTargetAmongSeveral()
    {
        var scenario = new PreviewScenario { ConfiguredWidthResolutions = [640, 320, 480] };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExpectedDefaultEntityTag, response.Headers.ETag?.Tag);
        Assert.Equal("MISS", response.Headers.GetValues("X-Trickplay-Cache").Single());
    }

    [Fact]
    public async Task ConcealsWhenTheSourceWidthClampsBelowEveryGeneratedResolution()
    {
        var scenario = new PreviewScenario { SourceVideoWidth = 300 };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemDetailsResponseAsync(response);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Equal(0, fixture.ErrorLogCount);
    }

    [Fact]
    public async Task ReusesCompatibleCacheEntryForAnEquivalentNormalizedTarget()
    {
        var scenario = new PreviewScenario { ConfiguredWidthResolutions = [321] };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);
        byte[] expectedContent = [0xFF, 0xD8, 1, 2, 3, 0xFF, 0xD9];
        string entryPath = GetDefaultEntryPath(fixture);
        Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
        await File.WriteAllBytesAsync(entryPath, expectedContent, CancellationToken.None);

        using HttpResponseMessage response = await fixture.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExpectedDefaultEntityTag, response.Headers.ETag?.Tag);
        Assert.Equal("HIT", response.Headers.GetValues("X-Trickplay-Cache").Single());
        Assert.Equal(expectedContent, await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
    }

}
