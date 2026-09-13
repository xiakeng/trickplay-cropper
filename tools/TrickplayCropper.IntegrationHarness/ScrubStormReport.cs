using System.Globalization;
using System.Net;
using System.Text;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Collects one Scrub Storm's GET outcomes and renders a redacted Markdown report.</summary>
public sealed class ScrubStormReport
{
    private readonly TimeProvider clock;
    private readonly object sync = new();
    private DateTimeOffset? startedUtc;
    private int gets;
    private int responses;
    private int unclassifiedResponses;
    private int transportFailures;
    private int hits;
    private int misses;
    private bool passed;

    public ScrubStormReport() : this(TimeProvider.System)
    {
    }

    public ScrubStormReport(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
    }

    /// <summary>Counts an actual GET dispatch and records its terminal response.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        BeginRequest(request.Method);
        try
        {
            HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            RecordResponse(response);
            return response;
        }
        catch
        {
            lock (sync)
            {
                transportFailures++;
            }

            throw;
        }
    }

    internal void MarkPassed() => passed = true;

    /// <summary>Summarizes actual attempts and terminal responses after the run has settled.</summary>
    public string ToMarkdown(bool harnessPassed)
    {
        lock (sync)
        {
            StringBuilder text = new("# Scrub Storm test report\n\n");
            text.AppendLine(CultureInfo.InvariantCulture, $"- First dispatch (UTC): **{(startedUtc is { } utc ? utc.ToString("O", CultureInfo.InvariantCulture) : "N/A")}**");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Scrub Storm outcome: **{(passed ? "Passed" : startedUtc is null ? "Not run" : "Failed or cancelled")}**");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Harness outcome (including restoration and health): **{(harnessPassed ? "Passed" : "Failed")}**");
            text.AppendLine(CultureInfo.InvariantCulture, $"- GET requests dispatched: **{gets}**");
            text.AppendLine(CultureInfo.InvariantCulture, $"- HTTP responses received: **{responses}**");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Cache HIT responses: **{hits}**");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Cache MISS responses: **{misses}**");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Non-200 or unclassified responses: **{unclassifiedResponses}**");
            text.AppendLine(CultureInfo.InvariantCulture, $"- Transport failures/cancellations: **{transportFailures}**");
            text.AppendLine("\n## Measurement definitions\n");
            text.AppendLine("- Seed: `0x5EEDC0DE`; two clients, three lanes/client, twelve positions/lane/Item, two rounds per shape.");
            text.AppendLine("- Counts cover only Scrub Storm Preview GET SendAsync invocations, including failed attempts; a transport failure can precede server receipt.");
            text.AppendLine("- Cache counts include only HTTP 200 responses with one exact HIT or MISS disposition; contract failures remain visible in the other totals.");
            text.AppendLine("- Metadata reads, deployment, quiescence, log/cache verification, and restoration are outside these GET totals.");
            return text.ToString();
        }
    }

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

    private void BeginRequest(HttpMethod method)
    {
        lock (sync)
        {
            if (method != HttpMethod.Get)
            {
                throw new ArgumentException("Only Scrub Storm GET requests may be measured.", nameof(method));
            }

            startedUtc ??= clock.GetUtcNow();
            gets++;
        }
    }

    private void RecordResponse(HttpResponseMessage response)
    {
        string category = ReadDisposition(response);
        lock (sync)
        {
            responses++;
            if (response.StatusCode == HttpStatusCode.OK && category.Length > 0)
            {
                if (category == "HIT")
                {
                    hits++;
                }
                else
                {
                    misses++;
                }
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
}
