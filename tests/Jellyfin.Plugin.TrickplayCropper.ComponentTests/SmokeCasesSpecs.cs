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

    [Theory]
    [InlineData("HEAD")]
    [InlineData("GET")]
    public async Task RejectsAnInvalidTokenThatDoesNotReturnUnauthorized(string method)
    {
        using AuthenticationResponses handler = new(method);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost:8096") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("MediaBrowser", "Token=\"abc123\"");
        using StringWriter output = new();
        await Assert.ThrowsAsync<InvalidDataException>(() => new SmokeCases(http, output).RunAsync(Input, CancellationToken.None));
        Assert.DoesNotContain("abc123", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsAnUnauthorizedHeadBody()
    {
        using AuthenticationResponses handler = new("body");
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost:8096") };
        using StringWriter output = new();
        await Assert.ThrowsAsync<InvalidDataException>(() => new SmokeCases(http, output).RunAsync(Input, CancellationToken.None));
    }

    [Fact]
    public async Task ChecksConcealmentAndBothPlaybackBoundariesAgainstIndependentMetadata()
    {
        using SmokeHostResponses handler = new();
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost:8096") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("MediaBrowser", "Token=\"abc123\"");
        using StringWriter output = new();
        await new SmokeCases(http, output).RunAsync(Input, CancellationToken.None);

        Assert.Contains("PASS concealed visibility: HEAD=404, GET=404", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("PASS Item 1 start: ticks=0, Frame Index=0", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("PASS Item 1 beyond-end: ticks=180000001, Frame Index=6", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("PASS Item 2 start: ticks=0, Frame Index=0", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("PASS Item 2 beyond-end: ticks=990000001, Frame Index=4", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(12, handler.BoundaryRequests);
        Assert.DoesNotContain("abc123", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("timing")]
    [InlineData("missing-length")]
    [InlineData("concealed-head")]
    [InlineData("concealed-get")]
    [InlineData("head-frame")]
    [InlineData("head-length")]
    [InlineData("head-body")]
    [InlineData("head-etag")]
    [InlineData("head-cache-policy")]
    [InlineData("get-frame")]
    [InlineData("get-weak-tag")]
    [InlineData("get-jpeg")]
    [InlineData("get-dimensions")]
    [InlineData("get-cache-policy")]
    [InlineData("get-disposition")]
    [InlineData("repeat-bytes")]
    [InlineData("repeat-tag")]
    [InlineData("repeat-miss")]
    [InlineData("second-item-frame")]
    public async Task RejectsBrokenPreviewContractsAndStillRestores(string fault)
    {
        using SmokeHostResponses handler = new(fault);
        using HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost:8096") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("MediaBrowser", "Token=\"abc123\"");
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

    private sealed class AuthenticationResponses(string rejectedMethod) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.DoesNotContain("abc123", request.Headers.Authorization!.ToString(), StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(request.Method.Method == rejectedMethod
                ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
            {
                Content = rejectedMethod == "body" ? new StringContent("unexpected body") : null,
            });
        }
    }
}
