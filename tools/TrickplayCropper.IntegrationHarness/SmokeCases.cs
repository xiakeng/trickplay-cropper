using System.Net;
using System.Net.Http.Headers;

namespace TrickplayCropper.IntegrationHarness;

/// <summary>Checks authentication, concealment, Timeline, and direct-index previews.</summary>
public sealed class SmokeCases(HttpClient http, TextWriter output)
{
    public async Task<IReadOnlyDictionary<Guid, PlaybackTimeline>> RunAsync(
        HarnessInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await VerifyAuthenticationAsync(input.PlayableItems[0], cancellationToken).ConfigureAwait(false);
        await VerifyConcealmentAsync(input.InvisibleItem, cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, PlaybackTimeline> timelines = [];
        foreach ((Guid item, int ordinal) in input.PlayableItems.Select((item, index) => (item, index + 1)))
        {
            PlaybackMetadata metadata = await PlaybackMetadata.ReadAsync(http, item, cancellationToken).ConfigureAwait(false);
            PlaybackTimeline timeline = await PlaybackMetadata.ReadTimelineAsync(http, item, cancellationToken)
                .ConfigureAwait(false);
            Require(timeline.IntervalTicks == checked((long)metadata.Interval * TimeSpan.TicksPerMillisecond)
                && timeline.FrameCount == metadata.Count, "Frame Timeline disagrees with independent metadata.");
            timelines.Add(item, timeline);
            output.WriteLine($"Reading Frame Timeline for Item {ordinal}: count={metadata.Count}, interval={metadata.Interval}ms.");
            foreach (int frameIndex in new[] { 0, metadata.LastFrameIndex })
            {
                await VerifyBoundaryAsync(new PreviewRequest(item, frameIndex, metadata), cancellationToken)
                    .ConfigureAwait(false);
            }

            using HttpResponseMessage outOfRange = await http.GetAsync(
                PreviewRoute(item, metadata.Count), cancellationToken).ConfigureAwait(false);
            Require(outOfRange.StatusCode == HttpStatusCode.BadRequest, "FrameIndex == frameCount must return 400.");
        }

        return timelines;
    }

    private async Task VerifyAuthenticationAsync(Guid item, CancellationToken cancellationToken)
    {
        foreach (string route in new[] { PreviewRoute(item, 0), TimelineRoute(item) })
        {
            using HttpRequestMessage request = new(HttpMethod.Get, route);
            request.Headers.Authorization = new AuthenticationHeaderValue("MediaBrowser", $"Token=\"{Guid.NewGuid():N}\"");
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            Require(response.StatusCode == HttpStatusCode.Unauthorized, "An invented invalid token must return 401.");
        }
    }

    private async Task VerifyConcealmentAsync(Guid item, CancellationToken cancellationToken)
    {
        foreach (string route in new[] { PreviewRoute(item, 0), TimelineRoute(item) })
        {
            using HttpResponseMessage response = await http.GetAsync(route, cancellationToken).ConfigureAwait(false);
            Require(response.StatusCode == HttpStatusCode.NotFound, "The invisible Item GET must return concealed 404.");
        }
    }

    private async Task VerifyBoundaryAsync(PreviewRequest preview, CancellationToken cancellationToken)
    {
        using HttpResponseMessage first = await http.GetAsync(preview.Route, cancellationToken).ConfigureAwait(false);
        byte[] original = await PreviewAssertions.VerifyJpegAsync(first, preview, cancellationToken).ConfigureAwait(false);
        using HttpResponseMessage repeat = await http.GetAsync(preview.Route, cancellationToken).ConfigureAwait(false);
        byte[] repeated = await PreviewAssertions.VerifyJpegAsync(repeat, preview, cancellationToken).ConfigureAwait(false);
        Require(repeat.Headers.GetValues("X-Trickplay-Cache").Single() == "HIT"
            && first.Headers.ETag!.Equals(repeat.Headers.ETag)
            && original.AsSpan().SequenceEqual(repeated), "Repeated GET must be a HIT with identical JPEG bytes and ETag.");
    }

    private static string PreviewRoute(Guid item, int frameIndex) =>
        FormattableString.Invariant($"/TrickplayCropper/Videos/{item:N}/Preview?FrameIndex={frameIndex}");

    private static string TimelineRoute(Guid item) =>
        FormattableString.Invariant($"/TrickplayCropper/Videos/{item:N}/FrameTimeline");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
