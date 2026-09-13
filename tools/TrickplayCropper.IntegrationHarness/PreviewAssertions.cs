using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Owns the shared HTTP acceptance contract for boundaries and Scrub Storm.</summary>
internal sealed class PreviewAssertions
{
    /// <summary>Checks a complete JPEG representation against independent metadata.</summary>
    public static async Task<byte[]> VerifyJpegAsync(HttpResponseMessage response, PreviewRequest preview, CancellationToken cancellationToken)
    {
        Require(response.StatusCode == HttpStatusCode.OK, "A playable GET must return 200.");
        Require(response.Content.Headers.ContentType?.ToString() == "image/jpeg"
            && response.Content.Headers.ContentDisposition?.ToString() == "inline", "GET must return an inline JPEG.");
        VerifyCacheControl(response);
        Require(!response.Headers.Contains("X-Trickplay-Frame-Index"), "GET must not expose a Frame Index header.");
        Require(Header(response, "X-Trickplay-Cache") is "MISS" or "HIT"
            && !response.Headers.Contains("X-Trickplay-Cache-File"), "GET must report a cache disposition without a private path.");
        Require(Regex.IsMatch(Header(response, "Server-Timing"),
            "^lookup;dur=[0-9]+\\.[0-9]{3}, cache;dur=[0-9]+\\.[0-9]{3}(, decode;dur=[0-9]+\\.[0-9]{3}, encode;dur=[0-9]+\\.[0-9]{3})?$",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), "GET must include valid lookup and cache timing stages.");
        string tag = response.Headers.ETag?.ToString() ?? string.Empty;
        Require(Regex.IsMatch(tag, "^\"[^\"]+\"$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
            "GET must return an opaque strong ETag.");
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        Require(response.Content.Headers.Contains("Content-Length") && response.Content.Headers.ContentLength == bytes.Length,
            "GET must declare Content-Length matching the JPEG bytes.");
        VerifyDecodedJpeg(bytes, preview.Metadata);
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

}
