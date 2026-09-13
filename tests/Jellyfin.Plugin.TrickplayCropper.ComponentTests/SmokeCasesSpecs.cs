using System.Net;
using System.Net.Http.Headers;
using TrickplayCropper.IntegrationHarness;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class SmokeCasesSpecs
{
    private static HarnessInput Input => HarnessInput.Parse("""
        {"adminToken":"abc123","playableItemIds":["11111111111111111111111111111111","22222222222222222222222222222222"],
         "invisibleItemId":"33333333333333333333333333333333"}
        """);

    [Fact]
    public async Task ChecksTimelineBoundariesRepeatedJpegsAndConcealment()
    {
        using SmokeHostResponses handler = new();
        using HttpClient http = CreateClient(handler);
        using StringWriter output = new();

        IReadOnlyDictionary<Guid, PlaybackTimeline> timelines =
            await new SmokeCases(http, output).RunAsync(Input, CancellationToken.None);

        Assert.Equal(7, timelines[Input.PlayableItems[0]].FrameCount);
        Assert.Equal(5, timelines[Input.PlayableItems[1]].FrameCount);
        Assert.Contains("Reading Frame Timeline for Item 1: count=7, interval=2500ms.", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Reading Frame Timeline for Item 2: count=5, interval=2500ms.", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(10, handler.BoundaryRequests);
        Assert.DoesNotContain("abc123", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsAnInvalidTokenWithoutEchoingIt()
    {
        using SmokeHostResponses handler = new();
        using HttpClient http = CreateClient(handler);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("MediaBrowser", "Token=\"wrong\"");
        using StringWriter output = new();

        await Assert.ThrowsAsync<InvalidDataException>(() => new SmokeCases(http, output).RunAsync(Input, CancellationToken.None));
        Assert.DoesNotContain("wrong", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("timing")]
    [InlineData("missing-length")]
    [InlineData("concealed-get")]
    [InlineData("get-weak-tag")]
    [InlineData("get-jpeg")]
    [InlineData("get-dimensions")]
    [InlineData("get-cache-policy")]
    [InlineData("get-disposition")]
    [InlineData("repeat-bytes")]
    [InlineData("repeat-tag")]
    [InlineData("repeat-miss")]
    public async Task RestoresAfterAContractFailure(string fault)
    {
        using SmokeHostResponses handler = new(fault);
        using HttpClient http = CreateClient(handler);
        using StringWriter output = new();
        bool restored = false;

        bool passed = await new DeploymentCycle(output).RunAsync(
            () => Task.FromResult(0),
            () => new SmokeCases(http, output).RunAsync(Input, CancellationToken.None),
            () => { restored = true; return Task.CompletedTask; });

        Assert.False(passed);
        Assert.True(restored);
        Assert.Contains("restoration restart is healthy", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", output.ToString(), StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost:8096") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("MediaBrowser", "Token=\"abc123\"");
        return http;
    }
}
