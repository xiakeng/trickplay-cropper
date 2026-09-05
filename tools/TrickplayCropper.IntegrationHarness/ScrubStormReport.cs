using System.Globalization;
using System.Net;
using System.Text;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Collects one Scrub Storm's client-side HTTP timings and renders a redacted Markdown report.</summary>
public sealed class ScrubStormReport
{
    private readonly TimeProvider clock;
    private readonly object sync = new();
    private readonly List<(string Category, double Milliseconds)> samples = [];
    private DateTimeOffset? startedUtc;
    private long? firstSend;
    private long lastSend;
    private long? lastCompletion;
    private int heads;
    private int gets;
    private int responses;
    private int unclassifiedResponses;
    private int transportFailures;
    private bool passed;

    /// <summary>Measures elapsed time with the system's monotonic clock.</summary>
    public ScrubStormReport() : this(TimeProvider.System)
    {
    }

    /// <summary>Uses the supplied monotonic clock for repeatable measurement contracts.</summary>
    public ScrubStormReport(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
    }

    /// <summary>Counts an actual dispatch and times the complete buffered HTTP response, excluding local assertions.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        long started = BeginRequest(request.Method);
        try
        {
            HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            long completed = clock.GetTimestamp();
            RecordResponse(request.Method, response, (started, completed));
            return response;
        }
        catch
        {
            long completed = clock.GetTimestamp();
            lock (sync)
            {
                transportFailures++;
                lastCompletion = Math.Max(lastCompletion ?? completed, completed);
            }

            throw;
        }
    }

    /// <summary>Marks the case passed only after its HTTP, representation, log, and filesystem checks succeed.</summary>
    internal void MarkPassed() => passed = true;

    /// <summary>Summarizes actual attempts, terminal responses, and descriptive timings after the run has settled.</summary>
    public string ToMarkdown(bool harnessPassed)
    {
        lock (sync)
        {
            StringBuilder text = new("# Scrub Storm test report\n\n");
            text.AppendLine(CultureInfo.InvariantCulture, $"- First dispatch (UTC): **{(startedUtc is { } utc ? utc.ToString("O", System.Globalization.CultureInfo.InvariantCulture) : "N/A")}**");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Scrub Storm outcome: **{(passed ? "Passed" : firstSend is null ? "Not run" : "Failed or cancelled")}**");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Harness outcome (including restoration and health): **{(harnessPassed ? "Passed" : "Failed")}**");
            AppendTotals(text);
            text.AppendLine("\n## Response times\n");
            text.AppendLine("| Category | Samples | Minimum (ms) | Maximum (ms) | Median (ms) | Mean (ms) |");
            text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
            AppendStatistics(text, "HEAD", "HEAD");
            AppendStatistics(text, "MISS", "GET cache MISS");
            AppendStatistics(text, "HIT", "GET cache HIT");
            AppendDefinitions(text);
            return text.ToString();
        }
    }

    /// <summary>Writes a unique local report after restoration, retaining previous runs and cancelled-run measurements.</summary>
    public async Task<string> WriteAsync(string directory, bool harnessPassed)
    {
        Directory.CreateDirectory(directory);
        string name = FormattableString.Invariant($"scrub-storm-{clock.GetUtcNow():yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.md");
        string path = Path.Combine(directory, name);
        await using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await using StreamWriter writer = new(stream, new UTF8Encoding(false));
        await writer.WriteAsync(ToMarkdown(harnessPassed)).ConfigureAwait(false);
        return path;
    }

    private long BeginRequest(HttpMethod method)
    {
        lock (sync)
        {
            if (method != HttpMethod.Head && method != HttpMethod.Get)
            {
                throw new ArgumentException("Only Scrub Storm HEAD and GET requests may be measured.", nameof(method));
            }

            long timestamp = clock.GetTimestamp();
            firstSend ??= timestamp;
            startedUtc ??= clock.GetUtcNow();
            lastSend = timestamp;
            heads += method == HttpMethod.Head ? 1 : 0;
            gets += method == HttpMethod.Get ? 1 : 0;
            return timestamp;
        }
    }

    private void RecordResponse(HttpMethod method, HttpResponseMessage response, (long Start, long End) interval)
    {
        string category = method == HttpMethod.Head ? "HEAD" : ReadDisposition(response);
        lock (sync)
        {
            responses++;
            lastCompletion = Math.Max(lastCompletion ?? interval.End, interval.End);
            if (response.StatusCode == HttpStatusCode.OK && category.Length > 0)
            {
                samples.Add((category, clock.GetElapsedTime(interval.Start, interval.End).TotalMilliseconds));
            }
            else
            {
                unclassifiedResponses++;
            }
        }
    }

    private static string ReadDisposition(HttpResponseMessage response)
    {
        string[] values = response.Headers.TryGetValues("X-Trickplay-Cache", out IEnumerable<string>? headers) ? headers.ToArray() : [];
        return values.Length == 1 && values[0] is "HIT" or "MISS" ? values[0] : string.Empty;
    }

    private void AppendTotals(StringBuilder text)
    {
        double elapsed = firstSend is { } first && lastCompletion is { } last ? clock.GetElapsedTime(first, last).TotalSeconds : 0;
        double span = firstSend is { } start ? clock.GetElapsedTime(start, lastSend).TotalSeconds : 0;
        text.AppendLine(FormattableString.Invariant($"- Request dispatch span: **{span:F3} s**"));
        text.AppendLine(FormattableString.Invariant($"- HTTP workload elapsed: **{elapsed:F3} s**"));
        text.AppendLine(FormattableString.Invariant($"- HEAD requests dispatched: **{heads}**"));
        text.AppendLine(FormattableString.Invariant($"- GET requests dispatched: **{gets}**"));
        text.AppendLine(FormattableString.Invariant($"- HTTP responses received: **{responses}**"));
        text.AppendLine(FormattableString.Invariant($"- Cache HIT responses: **{samples.Count(sample => sample.Category == "HIT")}**"));
        text.AppendLine(FormattableString.Invariant($"- Cache MISS responses: **{samples.Count(sample => sample.Category == "MISS")}**"));
        text.AppendLine(FormattableString.Invariant($"- Non-200 or unclassified responses: **{unclassifiedResponses}**"));
        text.AppendLine(FormattableString.Invariant($"- Transport failures/cancellations: **{transportFailures}**"));
    }

    private void AppendStatistics(StringBuilder text, string category, string label)
    {
        double[] durations = samples.Where(sample => sample.Category == category).Select(sample => sample.Milliseconds).Order().ToArray();
        if (durations.Length == 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {label} | 0 | N/A | N/A | N/A | N/A |");
            return;
        }

        int middle = durations.Length / 2;
        double median = durations.Length % 2 == 0 ? (durations[middle - 1] / 2) + (durations[middle] / 2) : durations[middle];
        text.AppendLine(FormattableString.Invariant(
            $"| {label} | {durations.Length} | {durations[0]:F3} | {durations[^1]:F3} | {median:F3} | {durations.Average():F3} |"));
    }

    private static void AppendDefinitions(StringBuilder text)
    {
        text.AppendLine("\n## Measurement definitions\n");
        text.AppendLine("- Seed: `0x5EEDC0DE`; two clients, three lanes/client, twelve positions/lane/Item, two rounds per shape.");
        text.AppendLine("- Dispatch span: first SendAsync invocation to last invocation. HTTP workload elapsed: first invocation to last completion, including scheduling gaps.");
        text.AppendLine("- Counts cover only Scrub Storm HEAD/GET SendAsync invocations, including failed attempts; a transport failure can precede server receipt.");
        text.AppendLine("- Response time uses a monotonic clock from dispatch until the complete response body is buffered. Local assertions/JPEG decoding are excluded.");
        text.AppendLine("- Timing groups contain HTTP 200 responses only; GET groups require an exact HIT/MISS header. HTTP contract failures can still fail the case after measurement.");
        text.AppendLine("- Metadata reads, deployment, quiescence, log/cache verification, and restoration are excluded from HTTP workload elapsed.");
        text.AppendLine("- Median is the middle sorted sample, or the mean of the two middle samples. Empty groups are N/A. Timings are diagnostics, not pass thresholds.");
    }
}
