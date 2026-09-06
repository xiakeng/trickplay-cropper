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
        await VerifyApiKeyAsync(input, cancellationToken).ConfigureAwait(false);
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
                output.WriteLine($"Checking Item {ordinal} {(ticks == 0 ? "start" : "beyond-end")}: GET, repeated GET, conditional GET, then refreshed HEAD.");
                await VerifyBoundaryAsync(new PreviewRequest(item, ticks, metadata), cancellationToken).ConfigureAwait(false);
                output.WriteLine(FormattableString.Invariant(
                    $"PASS Item {ordinal} {(ticks == 0 ? "start" : "beyond-end")}: ticks={ticks}, Frame Index={metadata.FrameIndex(ticks)}; HEAD=200/bodyless, GET=200/JPEG, repeat=HIT/identical bytes and ETag, conditional GET=304/current Frame Index."));
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

    private async Task VerifyApiKeyAsync(HarnessInput input, CancellationToken cancellationToken)
    {
        if (input.UserlessApiKey.Length == 0)
        {
            output.WriteLine("NOT RUN userless API key: no optional userlessApiKey supplied. Revocation/global-policy mutation is not exercised.");
            return;
        }

        using HttpRequestMessage currentUser = new(HttpMethod.Get, "/Users/Me");
        currentUser.Headers.Authorization = new AuthenticationHeaderValue("MediaBrowser", $"Token=\"{input.UserlessApiKey}\"");
        using HttpResponseMessage user = await http.SendAsync(currentUser, cancellationToken).ConfigureAwait(false);
        Require(user.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            "The supplied API key must not resolve a current user.");
        foreach (HttpMethod method in new[] { HttpMethod.Head, HttpMethod.Get })
        {
            using HttpRequestMessage request = new(method, PreviewRoute(input.PlayableItems[0], 0));
            request.Headers.Authorization = currentUser.Headers.Authorization;
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            Require(response.StatusCode == (method == HttpMethod.Head ? HttpStatusCode.OK : HttpStatusCode.Forbidden),
                "A userless API key must be accepted for HEAD but forbidden for GET.");
            if (method == HttpMethod.Head)
            {
                await PreviewAssertions.VerifyHeadAsync(response, 0, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Require(!response.Headers.Contains("X-Trickplay-Frame-Index"), "Forbidden GET must not expose a Frame Index.");
            }
        }

        output.WriteLine("PASS userless API key: HEAD=200, GET=403; no current user. Revocation/global-policy mutation is not exercised.");
    }

    private async Task VerifyConcealmentAsync(Guid item, CancellationToken cancellationToken)
    {
        output.WriteLine("Checking concealed GET visibility without treating HEAD as permission evidence.");
        using HttpResponseMessage response = await http.GetAsync(
            PreviewRoute(item, 0),
            cancellationToken).ConfigureAwait(false);
        Require(response.StatusCode == HttpStatusCode.NotFound, "The invisible Item GET must return concealed 404.");
        Require(
            !response.Headers.Contains("X-Trickplay-Frame-Index"),
            "A failed GET must not return a Frame Index.");
        output.WriteLine("PASS concealed response: GET=404; generation is unverified, so this alone does not prove authorization.");
    }

    private async Task VerifyBoundaryAsync(PreviewRequest boundary, CancellationToken cancellationToken)
    {
        string route = PreviewRoute(boundary.Item, boundary.Ticks);
        using HttpResponseMessage first = await http.GetAsync(route, cancellationToken).ConfigureAwait(false);
        byte[] original = await PreviewAssertions.VerifyJpegAsync(first, boundary, cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage repeat = await http.GetAsync(route, cancellationToken).ConfigureAwait(false);
        byte[] repeated = await PreviewAssertions.VerifyJpegAsync(repeat, boundary, cancellationToken).ConfigureAwait(false);
        Require(repeat.Headers.GetValues("X-Trickplay-Cache").Single() == "HIT" && first.Headers.ETag!.Equals(repeat.Headers.ETag)
            && original.AsSpan().SequenceEqual(repeated), "Repeated GET must be a HIT with identical JPEG bytes and ETag.");
        await VerifyConditionalAsync(boundary, repeat.Headers.ETag!, cancellationToken).ConfigureAwait(false);
        PlaybackMetadata current = await PlaybackMetadata.ReadAsync(http, boundary.Item, cancellationToken).ConfigureAwait(false);
        Require(current == boundary.Metadata, "The generated snapshot changed during smoke verification; rerun with stable fixtures.");
        // GET refreshes observations first: an older valid HEAD snapshot is not a GET parity oracle.
        using HttpRequestMessage request = new(HttpMethod.Head, route);
        using HttpResponseMessage head = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        Require(head.StatusCode == HttpStatusCode.OK, "A playable boundary HEAD must return 200.");
        await PreviewAssertions.VerifyHeadAsync(head, boundary.Metadata.FrameIndex(boundary.Ticks), cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyConditionalAsync(PreviewRequest boundary, EntityTagHeaderValue tag, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, boundary.Route);
        request.Headers.IfNoneMatch.Add(tag);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        Require(response.StatusCode == HttpStatusCode.NotModified, "Unchanged conditional GET must return 304.");
        Require(tag.Equals(response.Headers.ETag) && response.Headers.CacheControl?.ToString() == "no-cache, private",
            "Conditional GET must retain its representation ETag and cache policy.");
        Require(response.Headers.TryGetValues("X-Trickplay-Frame-Index", out IEnumerable<string>? frames)
            && frames.Single() == boundary.FrameIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Conditional GET must report its actual current Frame Index.");
        Require((await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)).Length == 0,
            "Conditional GET must be bodyless.");
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
