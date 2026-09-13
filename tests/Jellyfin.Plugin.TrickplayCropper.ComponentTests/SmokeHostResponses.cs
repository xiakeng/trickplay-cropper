using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SkiaSharp;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed class SmokeHostResponses(string fault = "") : HttpMessageHandler
{
    private const string FirstItem = "11111111111111111111111111111111";
    private const string SecondItem = "22222222222222222222222222222222";
    private readonly HashSet<string> cached = [];

    public int BoundaryRequests { get; private set; }

    public int TimelineRequests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string route = request.RequestUri!.PathAndQuery;
        if (request.Headers.Authorization?.Parameter != "Token=\"abc123\"")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        if (route.Contains("33333333333333333333333333333333", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(fault == "concealed-get" ? HttpStatusCode.OK : HttpStatusCode.NotFound));
        }

        if (route.Contains("/Preview?", StringComparison.Ordinal))
        {
            BoundaryRequests++;
            return Task.FromResult(Preview(request));
        }

        if (route.Contains("/FrameTimeline", StringComparison.Ordinal))
        {
            TimelineRequests++;
            bool first = route.Contains(FirstItem, StringComparison.Ordinal);
            int count = first ? 7 : 5;
            if (fault == "timeline-mismatch" && first)
            {
                count++;
            }

            return Task.FromResult(Json($"{{\"intervalTicks\":25000000,\"frameCount\":{count}}}"));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Metadata(route), Encoding.UTF8, "application/json"),
        });
    }

    private static string Metadata(string route)
    {
        if (route == "/System/Configuration")
        {
            return "{\"TrickplayOptions\":{\"WidthResolutions\":[640,321,321],\"Interval\":10000}}";
        }

        if (route == "/Users/Me")
        {
            return "{\"Id\":\"44444444444444444444444444444444\"}";
        }

        bool first = route.Contains(FirstItem, StringComparison.Ordinal);
        string item = first ? FirstItem : SecondItem;
        long runtime = first ? 180000000 : 990000000;
        if (route.Contains("PlaybackInfo", StringComparison.Ordinal))
        {
            return $"{{\"MediaSources\":[{{\"Id\":\"{item}\",\"RunTimeTicks\":{runtime},\"MediaStreams\":[{{\"Type\":\"Video\",\"Width\":1920}}]}}]}}";
        }

        int count = first ? 7 : 5;
        return "{\"Items\":[{\"Id\":\"ITEM\",\"Trickplay\":{\"ITEM\":{\"320\":{\"Width\":320,\"Height\":180,\"TileWidth\":10,\"TileHeight\":10,\"ThumbnailCount\":COUNT,\"Interval\":2500}}}}]}"
            .Replace("ITEM", item, StringComparison.Ordinal)
            .Replace("COUNT", count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private HttpResponseMessage Preview(HttpRequestMessage request)
    {
        string route = request.RequestUri!.PathAndQuery;
        int frame = int.Parse(request.RequestUri.Query.Split('=')[1], CultureInfo.InvariantCulture);
        bool first = route.Contains(FirstItem, StringComparison.Ordinal);
        int count = first ? 7 : 5;
        if (frame < 0 || frame >= count)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        string identity = (first ? FirstItem : SecondItem) + "/" + frame.ToString(CultureInfo.InvariantCulture);
        HttpResponseMessage response = new(HttpStatusCode.OK);
        response.Headers.CacheControl = new CacheControlHeaderValue { Private = true, NoCache = true };
        bool repeat = cached.Contains(identity);
        using SKBitmap bitmap = new(fault == "get-dimensions" ? 318 : 320, 180);
        bitmap.Erase(fault == "repeat-bytes" && repeat ? SKColors.Red : SKColors.DarkBlue);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        response.Content = fault == "missing-length"
            ? new UndeclaredLengthContent(jpeg.ToArray())
            : new ByteArrayContent(jpeg.ToArray());
        if (fault != "missing-length")
        {
            response.Content.Headers.ContentLength = jpeg.Size;
        }

        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline");
        response.Headers.ETag = new EntityTagHeaderValue(FormattableString.Invariant($"\"0123456789abcdef0123456789abcdef-f{frame:D10}\""));
        response.Headers.Add("X-Trickplay-Cache", cached.Add(identity) ? "MISS" : "HIT");
        response.Headers.Add("Server-Timing", "lookup;dur=1.000, cache;dur=1.000");
        CorruptGet(response, repeat);
        if (fault == "second-item-frame" && !first)
        {
            response.Headers.ETag = new EntityTagHeaderValue("\"0123456789abcdef0123456789abcdef-f0000000099\"");
        }

        return response;
    }

    private void CorruptGet(HttpResponseMessage response, bool repeat)
    {
        switch (fault)
        {
            case "timing":
                response.Headers.Remove("Server-Timing");
                break;
            case "get-weak-tag":
                response.Headers.ETag = new EntityTagHeaderValue(response.Headers.ETag!.Tag, true);
                break;
            case "get-jpeg":
                response.Content = new ByteArrayContent([0xff, 0xd8, 0xff, 0xd9]);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline");
                response.Content.Headers.ContentLength = 4;
                break;
            case "get-cache-policy":
                response.Headers.CacheControl!.MaxAge = TimeSpan.FromSeconds(60);
                break;
            case "get-disposition":
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
                break;
            case "repeat-tag" when repeat:
                response.Headers.ETag = new EntityTagHeaderValue(response.Headers.ETag!.Tag.Replace(
                    "0123456789abcdef0123456789abcdef", "fedcba9876543210fedcba9876543210", StringComparison.Ordinal));
                break;
            case "repeat-miss" when repeat:
                response.Headers.Remove("X-Trickplay-Cache");
                response.Headers.Add("X-Trickplay-Cache", "MISS");
                break;
        }
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    private sealed class UndeclaredLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
