using System.Net;
using System.Net.Http.Headers;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Checks fixed authentication, concealment, and playback boundaries through the live HTTP contract.</summary>
public sealed class SmokeCases(HttpClient http, TextWriter output)
{
    /// <summary>Runs the first three manual smoke cases without changing supplied credentials or subjects.</summary>
    public async Task RunAsync(HarnessInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await VerifyAuthenticationAsync(input.PlayableItems[0], cancellationToken).ConfigureAwait(false);
        await VerifyConcealmentAsync(input.InvisibleItem, cancellationToken).ConfigureAwait(false);
        foreach (Guid item in input.PlayableItems)
        {
            int ordinal = item == input.PlayableItems[0] ? 1 : 2;
            output.WriteLine($"Reading independent generated metadata for Item {ordinal}.");
            PlaybackMetadata metadata = await PlaybackMetadata.ReadAsync(http, item, cancellationToken).ConfigureAwait(false);
            output.WriteLine(FormattableString.Invariant(
                $"Item {ordinal} metadata: {metadata.Width}x{metadata.Height}, interval={metadata.Interval}ms, count={metadata.Count}, runtime={metadata.RuntimeTicks} ticks."));
            foreach (long ticks in new[] { 0L, metadata.BeyondEndTicks })
            {
                output.WriteLine($"Checking Item {ordinal} {(ticks == 0 ? "start" : "beyond-end")}: HEAD, GET, repeated GET.");
                await VerifyBoundaryAsync(new PreviewRequest(item, ticks, metadata), cancellationToken).ConfigureAwait(false);
                output.WriteLine(FormattableString.Invariant(
                    $"PASS Item {ordinal} {(ticks == 0 ? "start" : "beyond-end")}: ticks={ticks}, Frame Index={metadata.FrameIndex(ticks)}; HEAD=200/bodyless, GET=200/JPEG, repeat=HIT/identical bytes and ETag."));
            }
        }
    }

    private async Task VerifyAuthenticationAsync(Guid item, CancellationToken cancellationToken)
    {
        output.WriteLine("Checking invalid authentication: HEAD and GET.");
        foreach (HttpMethod method in new[] { HttpMethod.Head, HttpMethod.Get })
        {
            using HttpRequestMessage request = new(method, PreviewRoute(item, 0));
            request.Headers.Authorization = new AuthenticationHeaderValue("MediaBrowser", $"Token=\"{Guid.NewGuid():N}\"");
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            Require(response.StatusCode == HttpStatusCode.Unauthorized, "An invented invalid token must return 401.");
            if (method == HttpMethod.Head)
            {
                await PreviewAssertions.VerifyHeadAsync(response, null, cancellationToken).ConfigureAwait(false);
            }
        }

        output.WriteLine("PASS invalid authentication: HEAD=401, GET=401, empty HEAD body.");
    }

    private async Task VerifyConcealmentAsync(Guid item, CancellationToken cancellationToken)
    {
        output.WriteLine("Checking concealed visibility: HEAD and GET.");
        foreach (HttpMethod method in new[] { HttpMethod.Head, HttpMethod.Get })
        {
            using HttpRequestMessage request = new(method, PreviewRoute(item, 0));
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            Require(response.StatusCode == HttpStatusCode.NotFound, "The invisible Item must return concealed 404.");
            if (method == HttpMethod.Head)
            {
                await PreviewAssertions.VerifyHeadAsync(response, null, cancellationToken).ConfigureAwait(false);
            }
        }

        output.WriteLine("PASS concealed visibility: HEAD=404, GET=404, empty HEAD body.");
    }

    private async Task VerifyBoundaryAsync(PreviewRequest boundary, CancellationToken cancellationToken)
    {
        string route = PreviewRoute(boundary.Item, boundary.Ticks);
        using HttpRequestMessage request = new(HttpMethod.Head, route);
        using HttpResponseMessage head = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        Require(head.StatusCode == HttpStatusCode.OK, "A playable boundary HEAD must return 200.");
        await PreviewAssertions.VerifyHeadAsync(head, boundary.Metadata.FrameIndex(boundary.Ticks), cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage first = await http.GetAsync(route, cancellationToken).ConfigureAwait(false);
        byte[] original = await PreviewAssertions.VerifyJpegAsync(first, boundary, cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage repeat = await http.GetAsync(route, cancellationToken).ConfigureAwait(false);
        byte[] repeated = await PreviewAssertions.VerifyJpegAsync(repeat, boundary, cancellationToken).ConfigureAwait(false);
        Require(repeat.Headers.GetValues("X-Trickplay-Cache").Single() == "HIT" && first.Headers.ETag!.Equals(repeat.Headers.ETag)
            && original.AsSpan().SequenceEqual(repeated), "Repeated GET must be a HIT with identical JPEG bytes and ETag.");
    }

    private static string PreviewRoute(Guid item, long ticks) =>
        FormattableString.Invariant($"/TrickplayCropper/Videos/{item:N}/Preview?PositionTicks={ticks}");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

}
