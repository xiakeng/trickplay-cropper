using System.Globalization;
using System.Net;
using TrickplayCropper.IntegrationHarness;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public sealed class ScrubStormReportSpecs
{
    [Fact]
    public async Task ReportsActualCountsAndIndependentLatencyStatistics()
    {
        ManualClock clock = new();
        ScrubStormReport report = new(clock);
        using TimedResponses handler = new(clock);
        using HttpClient http = new(handler);
        foreach ((string category, int milliseconds) in new[]
        {
            ("HEAD", 1), ("HEAD", 9), ("HEAD", 3), ("HEAD", 7),
            ("MISS", 5), ("MISS", 20), ("MISS", 11), ("HIT", 2), ("HIT", 4),
        })
        {
            using HttpRequestMessage request = new(category == "HEAD" ? HttpMethod.Head : HttpMethod.Get,
                "http://localhost/preview?private-subject");
            request.Headers.Add("Test-Category", category);
            request.Headers.Add("Test-Duration", milliseconds.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("Authorization", "Bearer private-token");
            using HttpResponseMessage response = await report.SendAsync(http, request, CancellationToken.None);
        }

        string markdown = report.ToMarkdown(false);
        Assert.Contains("HEAD requests dispatched: **4**", markdown, StringComparison.Ordinal);
        Assert.Contains("GET requests dispatched: **5**", markdown, StringComparison.Ordinal);
        Assert.Contains("Cache HIT responses: **2**", markdown, StringComparison.Ordinal);
        Assert.Contains("Cache MISS responses: **3**", markdown, StringComparison.Ordinal);
        Assert.Contains("HTTP workload elapsed: **0.062 s**", markdown, StringComparison.Ordinal);
        Assert.Contains("Request dispatch span: **0.058 s**", markdown, StringComparison.Ordinal);
        Assert.Contains("| HEAD | 4 | 1.000 | 9.000 | 5.000 | 5.000 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| GET cache MISS | 3 | 5.000 | 20.000 | 11.000 | 12.000 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| GET cache HIT | 2 | 2.000 | 4.000 | 3.000 | 3.000 |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("private-", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritesSeparateMarkdownReportsAndMarksEmptyGroupsWithoutInventingSamples()
    {
        string directory = Path.Combine(Path.GetTempPath(), "storm-reports-" + Guid.NewGuid().ToString("N"));
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            ScrubStormReport report = new(new ManualClock());
            string first = await report.WriteAsync(directory, false);
            string second = await report.WriteAsync(directory, false);
            string markdown = await File.ReadAllTextAsync(first);
            Assert.NotEqual(first, second);
            Assert.Equal(2, Directory.GetFiles(directory, "*.md").Length);
            Assert.Equal(report.ToMarkdown(false), markdown);
            Assert.Contains("Scrub Storm outcome: **Not run**", markdown, StringComparison.Ordinal);
            Assert.Contains("Harness outcome (including restoration and health): **Failed**", markdown, StringComparison.Ordinal);
            Assert.Contains("HTTP workload elapsed: **0.000 s**", markdown, StringComparison.Ordinal);
            Assert.Contains("| HEAD | 0 | N/A | N/A | N/A | N/A |", markdown, StringComparison.Ordinal);
            Assert.Contains("| GET cache MISS | 0 | N/A | N/A | N/A | N/A |", markdown, StringComparison.Ordinal);
            Assert.Contains("| GET cache HIT | 0 | N/A | N/A | N/A | N/A |", markdown, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task MeasuresOverlappingRequestsWithoutSummingTheirDurations()
    {
        ManualClock clock = new();
        ScrubStormReport report = new(clock);
        using DeferredResponses handler = new();
        using HttpClient http = new(handler);
        using HttpRequestMessage head = new(HttpMethod.Head, "http://localhost/preview");
        using HttpRequestMessage get = new(HttpMethod.Get, "http://localhost/preview");
        Task<HttpResponseMessage> first = report.SendAsync(http, head, CancellationToken.None);
        clock.Advance(10);
        Task<HttpResponseMessage> second = report.SendAsync(http, get, CancellationToken.None);
        clock.Advance(20);
        HttpResponseMessage hit = new(HttpStatusCode.OK);
        hit.Headers.Add("X-Trickplay-Cache", "HIT");
        handler.Get.SetResult(hit);
        using HttpResponseMessage getResponse = await second;
        clock.Advance(10);
        handler.Head.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        using HttpResponseMessage headResponse = await first;

        string markdown = report.ToMarkdown(false);
        Assert.Contains("HTTP workload elapsed: **0.040 s**", markdown, StringComparison.Ordinal);
        Assert.Contains("Request dispatch span: **0.010 s**", markdown, StringComparison.Ordinal);
        Assert.Contains("| HEAD | 1 | 40.000 | 40.000 | 40.000 | 40.000 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| GET cache HIT | 1 | 20.000 | 20.000 | 20.000 | 20.000 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncludesResponseBodyTransferButExcludesLocalAssertionTime()
    {
        ManualClock clock = new();
        ScrubStormReport report = new(clock);
        using TimedResponses handler = new(clock);
        using HttpClient http = new(handler);
        using HttpRequestMessage request = new(HttpMethod.Get, "http://localhost/preview");
        request.Headers.Add("Test-Category", "HIT");
        request.Headers.Add("Test-Duration", "2");
        request.Headers.Add("Test-Body-Duration", "8");
        using HttpResponseMessage response = await report.SendAsync(http, request, CancellationToken.None);
        clock.Advance(20);
        string markdown = report.ToMarkdown(false);
        Assert.Contains("| GET cache HIT | 1 | 10.000 | 10.000 | 10.000 | 10.000 |", markdown, StringComparison.Ordinal);
        Assert.Contains("HTTP workload elapsed: **0.010 s**", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsFailedAttemptsWithoutTreatingThemAsSuccessfulLatencySamples()
    {
        ScrubStormReport report = new(new ManualClock());
        using FailureResponses handler = new();
        using HttpClient http = new(handler);
        using HttpRequestMessage head = new(HttpMethod.Head, "http://localhost/preview");
        using HttpRequestMessage get = new(HttpMethod.Get, "http://localhost/preview");
        using HttpResponseMessage response = await report.SendAsync(http, head, CancellationToken.None);
        await Assert.ThrowsAsync<HttpRequestException>(() => report.SendAsync(http, get, CancellationToken.None));
        string markdown = report.ToMarkdown(false);
        Assert.Contains("HEAD requests dispatched: **1**", markdown, StringComparison.Ordinal);
        Assert.Contains("GET requests dispatched: **1**", markdown, StringComparison.Ordinal);
        Assert.Contains("HTTP responses received: **1**", markdown, StringComparison.Ordinal);
        Assert.Contains("Non-200 or unclassified responses: **1**", markdown, StringComparison.Ordinal);
        Assert.Contains("Transport failures/cancellations: **1**", markdown, StringComparison.Ordinal);
        Assert.Contains("| HEAD | 0 | N/A | N/A | N/A | N/A |", markdown, StringComparison.Ordinal);
    }

    private sealed class DeferredResponses : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage> Head { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<HttpResponseMessage> Get { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.Method == HttpMethod.Head ? Head.Task : Get.Task;
    }

    private sealed class FailureResponses : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.Method == HttpMethod.Head ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))
                : Task.FromException<HttpResponseMessage>(new HttpRequestException("private-detail"));
    }

    private sealed class TimedContent(ManualClock clock, int milliseconds) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            clock.Advance(milliseconds);
            return stream.WriteAsync(new byte[] { 1 }).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 1;
            return true;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long milliseconds;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => milliseconds;

        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(milliseconds);

        public void Advance(int elapsed) => milliseconds += elapsed;
    }

    private sealed class TimedResponses(ManualClock clock) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            clock.Advance(int.Parse(request.Headers.GetValues("Test-Duration").Single(), CultureInfo.InvariantCulture));
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            if (request.Headers.TryGetValues("Test-Body-Duration", out IEnumerable<string>? values))
            {
                response.Content = new TimedContent(clock, int.Parse(values.Single(), CultureInfo.InvariantCulture));
            }

            if (request.Method == HttpMethod.Get)
            {
                response.Headers.Add("X-Trickplay-Cache", request.Headers.GetValues("Test-Category").Single());
            }

            return Task.FromResult(response);
        }
    }
}
