using System.Globalization;
using System.Net;
using System.Text.Json;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Reads generated Jellyfin metadata independently of plugin responses and implementation types.</summary>
internal sealed record PlaybackMetadata(int Width, int Height, int Interval, int Count, long RuntimeTicks)
{
    public long BeyondEndTicks => checked(RuntimeTicks + 1);

    public int FrameIndex(long ticks) => (int)Math.Min(ticks / checked((long)Interval * TimeSpan.TicksPerMillisecond), Count - 1L);

    public static async Task<PlaybackMetadata> ReadAsync(HttpClient http, Guid item, CancellationToken cancellationToken)
    {
        using JsonDocument user = await ReadJsonAsync(http, "/Users/Me", cancellationToken).ConfigureAwait(false);
        Guid userId = Guid.Parse(user.RootElement.GetProperty("Id").GetString()!);
        using JsonDocument playback = await ReadJsonAsync(http,
            $"/Items/{item:N}/PlaybackInfo?userId={userId:N}", cancellationToken).ConfigureAwait(false);
        JsonElement source = playback.RootElement.GetProperty("MediaSources").EnumerateArray()
            .Single(source => Guid.Parse(source.GetProperty("Id").GetString()!) == item);
        using JsonDocument configuration = await ReadJsonAsync(http, "/System/Configuration", cancellationToken).ConfigureAwait(false);
        int width = SelectWidth(configuration.RootElement, source);
        using JsonDocument items = await ReadJsonAsync(http,
            $"/Items?ids={item:N}&userId={userId:N}&fields=Trickplay&enableImages=false", cancellationToken).ConfigureAwait(false);
        JsonElement video = items.RootElement.GetProperty("Items").EnumerateArray()
            .Single(video => Guid.Parse(video.GetProperty("Id").GetString()!) == item);
        JsonElement resolutions = video.GetProperty("Trickplay").EnumerateObject()
            .Single(source => Guid.Parse(source.Name) == item).Value;
        return FromGeneratedMetadata(resolutions.GetProperty(width.ToString(CultureInfo.InvariantCulture)),
            width, source.GetProperty("RunTimeTicks").GetInt64());
    }

    private static int SelectWidth(JsonElement configuration, JsonElement source)
    {
        int[] targets = configuration.GetProperty("TrickplayOptions").GetProperty("WidthResolutions")
            .EnumerateArray().Select(target => target.GetInt32()).ToArray();
        if (targets.Length == 0 || targets.Any(target => target <= 0))
        {
            throw new InvalidDataException("Current Trickplay Resolution Targets must be positive and nonempty.");
        }

        JsonElement video = source.GetProperty("MediaStreams").EnumerateArray()
            .First(stream => stream.GetProperty("Type").GetString() == "Video");
        int target = targets.Min();
        int sourceWidth = video.TryGetProperty(nameof(Width), out JsonElement width) && width.ValueKind != JsonValueKind.Null
            ? width.GetInt32() : target;
        return Math.Min(target, sourceWidth) / 2 * 2;
    }

    private static PlaybackMetadata FromGeneratedMetadata(JsonElement metadata, int width, long runtime)
    {
        PlaybackMetadata result = new(metadata.GetProperty(nameof(Width)).GetInt32(), metadata.GetProperty(nameof(Height)).GetInt32(),
            metadata.GetProperty(nameof(Interval)).GetInt32(), metadata.GetProperty("ThumbnailCount").GetInt32(), runtime);
        if (result.Width != width || width <= 0 || result.Height <= 0 || result.Interval <= 0 || result.Count <= 0
            || runtime <= 0 || runtime == long.MaxValue)
        {
            throw new InvalidDataException("Generated Trickplay metadata or playback duration is invalid.");
        }

        return result;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient http, string route, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(route, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidDataException("A Jellyfin metadata endpoint rejected the request.");
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }
}
