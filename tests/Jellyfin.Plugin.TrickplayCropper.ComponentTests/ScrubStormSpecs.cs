using System.Net.Http.Headers;
using TrickplayCropper.IntegrationHarness;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class ScrubStormSpecs : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "storm-specs-" + Guid.NewGuid().ToString("N"));

    private static HarnessInput Input => HarnessInput.Parse("""
        {"adminToken":"abc123","playableItemIds":["11111111111111111111111111111111","22222222222222222222222222222222"],
         "invisibleItemId":"33333333333333333333333333333333"}
        """);

    public ScrubStormSpecs() => Directory.CreateDirectory(root);

    [Fact]
    public async Task ReplaysSixBarrierControlledLanesAndPassesWithoutContentionObservations()
    {
        using StormHostResponses handler = new(root);
        using HttpClient http = CreateClient(handler);
        using StringWriter output = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        ScrubStorm storm = new(http, output, root);
        await storm.RunAsync(Input, timeout.Token);

        string report = storm.Report.ToMarkdown(true);
        Assert.Contains("Scrub Storm outcome: **Passed**", report, StringComparison.Ordinal);
        Assert.Contains("HEAD requests dispatched: **864**", report, StringComparison.Ordinal);
        Assert.Contains("GET requests dispatched: **864**", report, StringComparison.Ordinal);
        Assert.Contains("Cache HIT responses: **852**", report, StringComparison.Ordinal);
        Assert.Contains("Cache MISS responses: **12**", report, StringComparison.Ordinal);
        Assert.Equal(1728, handler.Requests.Count);
        Assert.Equal(864, handler.Requests.Count(request => request.StartsWith("HEAD ", StringComparison.Ordinal)));
        Assert.Equal(864, handler.Requests.Count(request => request.Contains(" 1 ", StringComparison.Ordinal)));
        Assert.Equal(864, handler.Requests.Count(request => request.Contains(" 2 ", StringComparison.Ordinal)));
        foreach (int shape in Enumerable.Range(0, 3))
        {
            string[] firstRound = handler.Requests.Skip(shape * 576).Take(288).Order(StringComparer.Ordinal).ToArray();
            string[] secondRound = handler.Requests.Skip(shape * 576 + 288).Take(288).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(firstRound, secondRound);
        }

        // Worked trajectory anchors for the fixture's 7-frame Item at 2.5-second intervals.
        Assert.All(handler.Requests.Take(6), request => Assert.EndsWith("PositionTicks=0", request, StringComparison.Ordinal));
        Assert.All(handler.Requests.Skip(24).Take(6), request =>
            Assert.EndsWith("PositionTicks=150000000", request, StringComparison.Ordinal));
        Assert.All(handler.Requests.Skip(1152).Take(6), request =>
            Assert.EndsWith("PositionTicks=25000000", request, StringComparison.Ordinal));
        Assert.All(handler.Requests.Skip(1176).Take(6), request =>
            Assert.EndsWith("PositionTicks=125000000", request, StringComparison.Ordinal));
        Assert.Contains(handler.Requests.Skip(576).Take(6), request => request.EndsWith("PositionTicks=0", StringComparison.Ordinal));
        Assert.Contains(handler.Requests.Skip(576).Take(6), request => request.EndsWith("PositionTicks=150000000", StringComparison.Ordinal));
        Assert.Equal(12, (await CacheTreeSnapshot.ReadAsync(root, CancellationToken.None)).Count);
        Assert.Equal(1, handler.LogReads);
        Assert.Contains("MISS-to-HIT observed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Contention diagnostic entry-lock: not-observed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Contention diagnostic Cache Tree lease: not-observed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Contention diagnostic decode-permit: not-observed", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("repeat-bytes")]
    [InlineData("repeat-tag")]
    [InlineData("all-hit")]
    [InlineData("log-frame")]
    [InlineData("log-sprite")]
    [InlineData("log-disposition")]
    public async Task FailsHardConditionsAndStillRestores(string fault)
    {
        using StormHostResponses handler = new(root, fault);
        using HttpClient http = CreateClient(handler);
        using StringWriter output = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        bool restored = false;
        ScrubStorm storm = new(http, output, root);
        bool passed = await new DeploymentCycle(output).RunAsync(() => Task.FromResult(0),
            () => storm.RunAsync(Input, timeout.Token),
            () => { restored = true; return Task.CompletedTask; });

        Assert.Contains("Scrub Storm outcome: **Failed or cancelled**", storm.Report.ToMarkdown(passed), StringComparison.Ordinal);
        Assert.False(passed);
        Assert.True(restored);
        Assert.False(timeout.IsCancellationRequested);
        Assert.DoesNotContain("PASS Scrub Storm", output.ToString(), StringComparison.Ordinal);
        if (fault.StartsWith("log-", StringComparison.Ordinal))
        {
            Assert.Equal(2, handler.LogReads);
        }
    }

    [Fact]
    public async Task CancelsAllLanesBeforeRestorationWhenARequestIsInterrupted()
    {
        using StormHostResponses handler = new(root, "cancel");
        using HttpClient http = CreateClient(handler);
        using StringWriter output = new();
        using CancellationTokenSource cancellation = new();
        Task run = new ScrubStorm(http, output, root).RunAsync(Input, cancellation.Token);
        await handler.WaitingRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task WaitsForTemporaryPublicationResidueToDisappear()
    {
        using StormHostResponses handler = new(root, "temporary");
        using HttpClient http = CreateClient(handler);
        using StringWriter output = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        await new ScrubStorm(http, output, root).RunAsync(Input, timeout.Token);
        Assert.Equal(2, handler.LogReads);
        Assert.Contains("PASS Scrub Storm", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimesOutStalledRequestsWithoutRelyingOnTheHttpClientTimeout()
    {
        using StormHostResponses handler = new(root, "timeout");
        using HttpClient http = CreateClient(handler);
        using StringWriter output = new();
        Task run = new ScrubStorm(http, output, root).RunAsync(Input, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(6, handler.Requests.Count);
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        HttpClient http = new(handler) { BaseAddress = new Uri("http://localhost:8096"), Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("MediaBrowser", "Token=\"abc123\"");
        return http;
    }

    public void Dispose() => Directory.Delete(root, true);
}
