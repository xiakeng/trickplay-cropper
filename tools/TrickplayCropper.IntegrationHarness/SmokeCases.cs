using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using SkiaSharp;

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
                await VerifyBoundaryAsync(new Boundary(item, ticks, metadata), cancellationToken).ConfigureAwait(false);
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
                await VerifyHeadAsync(response, null, cancellationToken).ConfigureAwait(false);
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
                await VerifyHeadAsync(response, null, cancellationToken).ConfigureAwait(false);
            }
        }

        output.WriteLine("PASS concealed visibility: HEAD=404, GET=404, empty HEAD body.");
    }

    private async Task VerifyBoundaryAsync(Boundary boundary, CancellationToken cancellationToken)
    {
        string route = PreviewRoute(boundary.Item, boundary.Ticks);
        using HttpRequestMessage request = new(HttpMethod.Head, route);
        using HttpResponseMessage head = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        Require(head.StatusCode == HttpStatusCode.OK, "A playable boundary HEAD must return 200.");
        await VerifyHeadAsync(head, boundary.Metadata.FrameIndex(boundary.Ticks), cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage first = await http.GetAsync(route, cancellationToken).ConfigureAwait(false);
        byte[] original = await VerifyJpegAsync(first, boundary, cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage repeat = await http.GetAsync(route, cancellationToken).ConfigureAwait(false);
        byte[] repeated = await VerifyJpegAsync(repeat, boundary, cancellationToken).ConfigureAwait(false);
        Require(Header(repeat, "X-Trickplay-Cache") == "HIT" && first.Headers.ETag!.Equals(repeat.Headers.ETag)
            && original.AsSpan().SequenceEqual(repeated), "Repeated GET must be a HIT with identical JPEG bytes and ETag.");
    }

    private static async Task VerifyHeadAsync(HttpResponseMessage response, int? frame, CancellationToken cancellationToken)
    {
        Require(!response.Headers.Contains("ETag") && !response.Headers.Contains("Server-Timing")
            && !response.Headers.Contains("X-Trickplay-Cache") && !response.Headers.Contains("X-Trickplay-Cache-File")
            && response.Content.Headers.ContentType is null && response.Content.Headers.ContentDisposition is null
            && !response.Content.Headers.Contains("Content-Length"), "HEAD must omit GET representation and timing headers.");
        if (frame is int expected)
        {
            Require(Header(response, "X-Trickplay-Frame-Index") == expected.ToString(CultureInfo.InvariantCulture),
                "HEAD Frame Index disagrees with independently read metadata.");
            VerifyCacheControl(response);
        }
        else
        {
            Require(!response.Headers.Contains("X-Trickplay-Frame-Index") && !response.Headers.Contains("Cache-Control"),
                "Failed HEAD must not include successful probe headers.");
        }

        Require((await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)).Length == 0,
            "HEAD must have an empty body.");
    }

    private static async Task<byte[]> VerifyJpegAsync(HttpResponseMessage response, Boundary boundary, CancellationToken cancellationToken)
    {
        Require(response.StatusCode == HttpStatusCode.OK, "A playable boundary GET must return 200.");
        Require(response.Content.Headers.ContentType?.ToString() == "image/jpeg"
            && response.Content.Headers.ContentDisposition?.ToString() == "inline", "GET must return an inline JPEG.");
        VerifyCacheControl(response);
        Require(Header(response, "X-Trickplay-Cache") is "MISS" or "HIT"
            && !response.Headers.Contains("X-Trickplay-Cache-File"), "GET must report a cache disposition without a private path.");
        Require(Regex.IsMatch(Header(response, "Server-Timing"),
            "^lookup;dur=[0-9]+\\.[0-9]{3}, cache;dur=[0-9]+\\.[0-9]{3}(, decode;dur=[0-9]+\\.[0-9]{3}, encode;dur=[0-9]+\\.[0-9]{3})?$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), "GET must include valid lookup and cache timing stages.");
        string tag = response.Headers.ETag?.ToString() ?? string.Empty;
        string suffix = FormattableString.Invariant($"-f{boundary.Metadata.FrameIndex(boundary.Ticks):D10}\"");
        Require(Regex.IsMatch(tag, "^\"[0-9a-f]{32}-f[0-9]{10}\"$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            && tag.EndsWith(suffix, StringComparison.Ordinal), "GET strong ETag Frame Index disagrees with independently read metadata.");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        Require(response.Content.Headers.Contains("Content-Length") && response.Content.Headers.ContentLength == bytes.Length,
            "GET must declare Content-Length matching the JPEG bytes.");
        VerifyDecodedJpeg(bytes, boundary.Metadata);
        return bytes;
    }

    private static void VerifyDecodedJpeg(byte[] bytes, PlaybackMetadata metadata)
    {
        Require(bytes.Length > 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[^2] == 0xff && bytes[^1] == 0xd9,
            "GET bytes must have JPEG markers.");
        using SKData data = SKData.CreateCopy(bytes);
        using SKCodec? codec = SKCodec.Create(data);
        Require(codec is not null && codec.EncodedFormat == SKEncodedImageFormat.Jpeg
            && codec.Info.Width == metadata.Width && codec.Info.Height == metadata.Height, "JPEG dimensions disagree with generated metadata.");
        using SKBitmap bitmap = new(codec!.Info);
        Require(codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success, "GET must decode as a complete JPEG.");
    }

    private static void VerifyCacheControl(HttpResponseMessage response) =>
        Require(response.Headers.CacheControl?.ToString() == "no-cache, private", "The cache policy must be exactly private, no-cache.");

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? string.Join(",", values) : string.Empty;

    private static string PreviewRoute(Guid item, long ticks) =>
        FormattableString.Invariant($"/TrickplayCropper/Videos/{item:N}/Preview?PositionTicks={ticks}");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private sealed record Boundary(Guid Item, long Ticks, PlaybackMetadata Metadata);
}
