using System.Net;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Runs the fourth smoke case against real HTTP, the retained Cache Tree, and the newest server log.</summary>
public sealed class ScrubStorm(HttpClient http, TextWriter output, string cacheRoot)
{
    private static readonly TimeSpan requestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan quiescenceTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] shapes = ["random-jump", "large-range fast-sweep", "small-range precise-drag"];

    /// <summary>Gets this case's HTTP measurements, including any partial run before an assertion fails.</summary>
    public ScrubStormReport Report { get; } = new();

    /// <summary>Runs two replay rounds for each approved shape and enforces every hard acceptance condition.</summary>
    public async Task RunAsync(HarnessInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        PreviewRequest[] subjects = await ReadSubjectsAsync(input, cancellationToken).ConfigureAwait(false);
        StormObservations observations = new(await CacheTreeSnapshot.ReadAsync(cacheRoot, cancellationToken).ConfigureAwait(false));
        DateTimeOffset since = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        output.WriteLine("Checking Scrub Storm: seed=0x5EEDC0DE, clients=2, lanes/client=3, positions/lane/Item=12, rounds/shape=2.");
        foreach (int shape in Enumerable.Range(0, shapes.Length))
        {
            PreviewRequest[][] lanes = ScrubStormPlan.Create(subjects, shape);
            RequireIdentities(lanes);
            foreach (int round in Enumerable.Range(0, 2))
            {
                output.WriteLine($"Scrub Storm {shapes[shape]}, round {round + 1}: HEAD fan-out then GET fan-out.");
                await RunRoundAsync(lanes, observations, (shape * 2) + round, cancellationToken).ConfigureAwait(false);
            }
        }

        observations.VerifyTransition();
        output.WriteLine("Scrub Storm HTTP passed: 864 HEAD and 864 GET; stable repeated JPEG bytes/ETags and MISS-to-HIT observed.");
        await VerifyQuiescenceAsync(observations, since, cancellationToken).ConfigureAwait(false);
        Report.MarkPassed();
        output.WriteLine($"PASS Scrub Storm: {observations.IdentityCount} distinct Preview identities; canonical JPEGs and no temporary residue.");
    }

    private async Task<PreviewRequest[]> ReadSubjectsAsync(HarnessInput input, CancellationToken cancellationToken)
    {
        List<PreviewRequest> subjects = [];
        foreach (Guid item in input.PlayableItems)
        {
            PlaybackMetadata metadata = await PlaybackMetadata.ReadAsync(http, item, cancellationToken).ConfigureAwait(false);
            subjects.Add(new PreviewRequest(item, 0, metadata));
        }

        return subjects.ToArray();
    }

    private static void RequireIdentities(PreviewRequest[][] lanes)
    {
        var identities = lanes.SelectMany(lane => lane).GroupBy(preview => (preview.Item, preview.FrameIndex)).ToArray();
        if (identities.Length < 5 || !identities.Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("Supplied generated metadata cannot support five distinct Scrub Storm identities and a repeat.");
        }
    }

    private async Task RunRoundAsync(PreviewRequest[][] lanes, StormObservations observations, int round,
        CancellationToken cancellationToken)
    {
        await RunPhaseAsync(lanes, HttpMethod.Head, round, cancellationToken).ConfigureAwait(false);
        StormObservations.Response[] responses = await RunPhaseAsync(lanes, HttpMethod.Get, round, cancellationToken).ConfigureAwait(false);
        observations.Record(responses);
    }

    private async Task<StormObservations.Response[]> RunPhaseAsync(PreviewRequest[][] lanes, HttpMethod method, int round,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource phase = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StormBarrier[] barriers = Enumerable.Range(0, lanes[0].Length).Select(_ => new StormBarrier()).ToArray();
        Task<StormObservations.Response[]>[] tasks = lanes.Select((lane, index) =>
            RunLaneAsync(new Lane(lane, barriers, method, round, index), phase)).ToArray();
        StormObservations.Response[][] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.SelectMany(result => result).ToArray();
    }

    private async Task<StormObservations.Response[]> RunLaneAsync(Lane lane, CancellationTokenSource phase)
    {
        List<StormObservations.Response> responses = [];
        try
        {
            foreach (int position in Enumerable.Range(0, lane.Requests.Length))
            {
                await lane.Barriers[position].ArriveAsync(phase.Token).ConfigureAwait(false);
                StormObservations.Response? response = await SendAsync(lane, position, phase.Token).ConfigureAwait(false);
                if (response is not null)
                {
                    responses.Add(response);
                }
            }
        }
        catch
        {
            // A failed lane must release peers waiting at the next barrier before restoration.
            await phase.CancelAsync().ConfigureAwait(false);
            throw;
        }

        return responses.ToArray();
    }

    private async Task<StormObservations.Response?> SendAsync(Lane lane, int position, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout);
        PreviewRequest preview = lane.Requests[position];
        using HttpRequestMessage request = new(lane.Method, preview.Route);
        request.Headers.Add("X-Trickplay-Harness-Client", (lane.Index / ScrubStormPlan.LanesPerClient + 1)
            .ToString(System.Globalization.CultureInfo.InvariantCulture));
        using HttpResponseMessage response = await Report.SendAsync(http, request, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidDataException("Every Scrub Storm request must return 200.");
        }

        if (lane.Method == HttpMethod.Head)
        {
            await PreviewAssertions.VerifyHeadAsync(response, preview.FrameIndex, timeout.Token).ConfigureAwait(false);
            return null;
        }

        byte[] bytes = await PreviewAssertions.VerifyJpegAsync(response, preview, timeout.Token).ConfigureAwait(false);
        timeout.Token.ThrowIfCancellationRequested();
        return new StormObservations.Response(preview, response.Headers.ETag!.ToString(), bytes,
            response.Headers.GetValues("X-Trickplay-Cache").Single(), (lane.Round * lane.Requests.Length) + position);
    }

    private async Task VerifyQuiescenceAsync(StormObservations observations, DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(quiescenceTimeout);
        LocalJellyfin host = new(http);
        while (true)
        {
            string log = await host.ReadNewestLogAsync(timeout.Token).ConfigureAwait(false);
            IReadOnlyList<DebugEventReader.ProtocolEvent> events = DebugEventReader.Read(log, since, timeout.Token);
            if (observations.Matches(events)
                && await CacheTreeSnapshot.MatchesAsync(cacheRoot, observations.Files, timeout.Token).ConfigureAwait(false))
            {
                timeout.Token.ThrowIfCancellationRequested();
                output.WriteLine("Stable Debug events reconciled: GET disposition counts and Frame Index/sprite index multiplicities agree.");
                output.WriteLine($"Reconciled GET events: MISS={events.Count(value => value.Disposition == "MISS")}, "
                    + $"HIT={events.Count(value => value.Disposition == "HIT")}, "
                    + $"FrameSelected={events.Count(value => value.EventId == 1002)}.");
                ReportWaits(events);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token).ConfigureAwait(false);
        }
    }

    private void ReportWaits(IReadOnlyList<DebugEventReader.ProtocolEvent> events)
    {
        foreach ((int id, string name) in new[] { (1004, "entry-lock"), (1006, "Cache Tree lease"), (1007, "decode-permit") })
        {
            output.WriteLine($"Contention diagnostic {name}: {(events.Any(value => value.EventId == id) ? "observed" : "not-observed")}.");
        }
    }

    private sealed record Lane(PreviewRequest[] Requests, StormBarrier[] Barriers, HttpMethod Method, int Round, int Index);
}
