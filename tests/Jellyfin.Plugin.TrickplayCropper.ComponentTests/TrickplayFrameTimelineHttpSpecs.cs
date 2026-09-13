using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class TrickplayFrameTimelineHttpSpecs
{
    [Fact]
    public async Task ReturnsTheAuthorizedTimelineWithoutImageWork()
    {
        var scenario = new PreviewScenario
        {
            MetadataIntervalMilliseconds = 300_000,
        };
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateAsync(scenario);

        using HttpResponseMessage response = await fixture.GetFrameTimelineAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.False(response.Headers.Contains("ETag"));
        Assert.Null(response.Content.Headers.LastModified);
        Assert.Equal(1, scenario.MetadataReadCount);
        Assert.Equal(0, scenario.SourceSpritePathRequests);
        Assert.Equal(0, fixture.Cache.CallCount);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ["frameCount", "intervalTicks"],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(3_000_000_000L, body.RootElement.GetProperty("intervalTicks").GetInt64());
        Assert.Equal(4, body.RootElement.GetProperty("frameCount").GetInt32());

        using HttpResponseMessage conditional = await fixture.GetFrameTimelineConditionalAsync("\"ignored\"");
        Assert.Equal(HttpStatusCode.OK, conditional.StatusCode);
    }

    [Theory]
    [InlineData(AuthenticationState.Missing, HttpStatusCode.Unauthorized)]
    [InlineData(AuthenticationState.ApiKeyWithoutCurrentUser, HttpStatusCode.Forbidden)]
    public async Task PreservesCurrentUserAuthorizationFailures(
        AuthenticationState authentication,
        HttpStatusCode expectedStatus)
    {
        await using PreviewHostFixture fixture = await PreviewHostFixture.CreateWithKestrelAsync(
            new PreviewScenario { Authentication = authentication });

        using HttpResponseMessage response = await fixture.GetFrameTimelineAsync();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(0, fixture.Services.GetRequiredService<PreviewScenario>().MetadataReadCount);
    }

    [Fact]
    public async Task MapsConcealmentAndInvalidGeneratedData()
    {
        await using PreviewHostFixture concealed = await PreviewHostFixture.CreateWithKestrelAsync(
            new PreviewScenario { LogicalVideo = ItemAvailability.Missing });
        using (HttpResponseMessage notFound = await concealed.GetFrameTimelineAsync())
        {
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        }

        await using PreviewHostFixture invalid = await PreviewHostFixture.CreateWithKestrelAsync(
            new PreviewScenario { Metadata = MetadataAvailability.IntervalZero });
        using HttpResponseMessage internalError = await invalid.GetFrameTimelineAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, internalError.StatusCode);

        await using PreviewHostFixture geometry = await PreviewHostFixture.CreateAsync(
            new PreviewScenario { Metadata = MetadataAvailability.TileWidthZero });
        using HttpResponseMessage timeline = await geometry.GetFrameTimelineAsync();
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
    }
}
