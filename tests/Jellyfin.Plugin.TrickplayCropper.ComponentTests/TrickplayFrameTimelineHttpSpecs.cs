using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayFrameTimelineHttpSpecs
{
    [Fact]
    public async Task ReturnsOnlyAuthoritativeTimelineFactsWithoutImageWork()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(
            new PreviewScenario { RequestPositionTicks = 40_000L * TimeSpan.TicksPerMillisecond });

        using HttpResponseMessage response = await fixture.GetFrameTimelineAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.False(response.Headers.Contains("ETag"));
        Assert.False(response.Content.Headers.LastModified is not null);
        Assert.Equal(
            "{\"intervalTicks\":100000000,\"frameCount\":4}",
            await response.Content.ReadAsStringAsync());
        Assert.Equal(1, fixture.Services.GetRequiredService<PreviewScenario>().MetadataReadCount);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
        Assert.Empty(fixture.EnumerateCacheFiles());
    }

    [Fact]
    public async Task RejectsMalformedTimelineIdentifiersBeforeHostReads()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync();
        using HttpResponseMessage response = await fixture.Client.GetAsync(
            "/TrickplayCropper/Videos/not-a-guid/FrameTimeline");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fixture.Services.GetRequiredService<PreviewScenario>().MetadataReadCount);
    }

    [Theory]
    [InlineData(AuthenticationState.ApiKeyWithoutCurrentUser, HttpStatusCode.Forbidden)]
    [InlineData(AuthenticationState.Missing, HttpStatusCode.Unauthorized)]
    public async Task PreservesCurrentUserAuthenticationBoundaries(
        AuthenticationState authentication,
        HttpStatusCode expectedStatus)
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(
            new PreviewScenario { Authentication = authentication });

        using HttpResponseMessage response = await fixture.GetFrameTimelineAsync();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(0, fixture.Services.GetRequiredService<PreviewScenario>().MetadataReadCount);
    }

    [Fact]
    public async Task MapsInvalidGeneratedTimelineDataToInternalServerError()
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(
            new PreviewScenario { Metadata = MetadataAvailability.IntervalZero });

        using HttpResponseMessage response = await fixture.GetFrameTimelineAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, fixture.Services.GetRequiredService<PreviewScenario>().MetadataReadCount);
        Assert.Equal(0, fixture.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);
    }
}
