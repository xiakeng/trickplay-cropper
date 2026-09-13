using System.Globalization;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.TrickplayCropper.Api;

/// <summary>
/// Exposes authenticated Trickplay Previews over HTTP.
/// </summary>
[ApiController]
[Route("TrickplayCropper/Videos/{itemId}/Preview")]
public sealed class TrickplayPreviewController : ControllerBase
{
    private const string CacheControlHeaderValue = "private, no-cache";
    private readonly ITrickplayPreview trickplayPreview;
    private readonly IFrameTimeline frameTimeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayPreviewController"/> class.
    /// </summary>
    /// <param name="trickplayPreview">The Trickplay Preview request module.</param>
    public TrickplayPreviewController(
        ITrickplayPreview trickplayPreview,
        IFrameTimeline frameTimeline)
    {
        this.trickplayPreview = trickplayPreview;
        this.frameTimeline = frameTimeline;
    }

    /// <summary>
    /// Gets one Trickplay Preview for the requested Frame Index.
    /// </summary>
    /// <param name="itemId">The logical video identifier.</param>
    /// <param name="parameters">The normalized query-string parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The mapped HTTP response.</returns>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromRoute] Guid itemId,
        [FromQuery] PreviewQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        var query = new PreviewQuery(itemId, parameters.MediaSourceId, parameters.FrameIndex);
        EntityTagHeaderValue[] conditionalEntityTags = Request.GetTypedHeaders().IfNoneMatch?.ToArray() ?? [];
        PreviewOutcome outcome = await trickplayPreview.GetAsync(
            query,
            User,
            conditionalEntityTags,
            cancellationToken).ConfigureAwait(false);

        return MapOutcome(outcome);
    }

    /// <summary>
    /// Gets the current generated frame interval and count for an authorized playback source.
    /// </summary>
    /// <param name="itemId">The logical video identifier.</param>
    /// <param name="mediaSourceId">The optional alternate media source identifier.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The mapped HTTP response.</returns>
    [Authorize]
    [HttpGet("~/TrickplayCropper/Videos/{itemId}/FrameTimeline")]
    public async Task<IActionResult> GetFrameTimelineAsync(
        [FromRoute] Guid itemId,
        [FromQuery] Guid? mediaSourceId,
        CancellationToken cancellationToken)
    {
        FrameTimelineOutcome outcome = await frameTimeline.GetAsync(
            new PreviewSourceQuery(itemId, mediaSourceId),
            User,
            cancellationToken).ConfigureAwait(false);
        return MapFrameTimelineOutcome(outcome);
    }

    private IActionResult MapFrameTimelineOutcome(FrameTimelineOutcome outcome)
    {
        return outcome switch
        {
            FrameTimelineOutcome.Success success => MapFrameTimelineSuccess(success),
            FrameTimelineOutcome.BadRequest => BadRequest(),
            FrameTimelineOutcome.Unauthorized => Unauthorized(),
            FrameTimelineOutcome.Forbidden => Forbid(),
            FrameTimelineOutcome.NotFound => NotFound(),
            FrameTimelineOutcome.InternalError => StatusCode(StatusCodes.Status500InternalServerError),
            _ => throw new InvalidOperationException(
                $"Unknown Frame Timeline outcome {outcome.GetType().Name}."),
        };
    }

    private JsonResult MapFrameTimelineSuccess(FrameTimelineOutcome.Success outcome)
    {
        Response.Headers.CacheControl = CacheControlHeaderValue;
        return new JsonResult(
            new
            {
                intervalTicks = outcome.IntervalTicks,
                frameCount = outcome.FrameCount,
            });
    }

    private IActionResult MapOutcome(PreviewOutcome outcome)
    {
        return outcome switch
        {
            PreviewOutcome.Ok ok => MapOk(ok),
            PreviewOutcome.NotModified notModified => MapNotModified(notModified),
            PreviewOutcome.BadRequest => BadRequest(),
            PreviewOutcome.Unauthorized => Unauthorized(),
            PreviewOutcome.Forbidden => Forbid(),
            PreviewOutcome.NotFound => NotFound(),
            PreviewOutcome.InternalError => StatusCode(StatusCodes.Status500InternalServerError),
            _ => throw new InvalidOperationException($"Unknown preview outcome {outcome.GetType().Name}."),
        };
    }

    private FileContentResult MapOk(PreviewOutcome.Ok outcome)
    {
        ApplySharedHeaders(outcome.EntityTag, outcome.Telemetry);
        Response.Headers.ContentDisposition = "inline";
        Response.Headers["X-Trickplay-Cache"] = outcome.Telemetry.CacheDisposition.ToString().ToUpperInvariant();
        byte[] content = outcome.Content.ToArray();
        Response.ContentLength = content.Length;
        return File(content, "image/jpeg");
    }

    private StatusCodeResult MapNotModified(PreviewOutcome.NotModified outcome)
    {
        ApplySharedHeaders(outcome.EntityTag, outcome.Telemetry);
        return StatusCode(StatusCodes.Status304NotModified);
    }

    private void ApplySharedHeaders(string entityTag, PreviewTelemetry telemetry)
    {
        Response.Headers.ETag = entityTag;
        Response.Headers.CacheControl = CacheControlHeaderValue;
        Response.Headers["Server-Timing"] = FormatServerTiming(telemetry);
    }

    private static string FormatServerTiming(PreviewTelemetry telemetry)
    {
        List<string> stages =
        [
            FormatTiming("lookup", telemetry.Lookup),
        ];

        AddTiming(stages, "cache", telemetry.Cache);
        AddTiming(stages, "decode", telemetry.Decode);
        AddTiming(stages, "encode", telemetry.Encode);
        return string.Join(", ", stages);
    }

    private static void AddTiming(List<string> stages, string name, TimeSpan? duration)
    {
        if (duration is not null)
        {
            stages.Add(FormatTiming(name, duration.Value));
        }
    }

    private static string FormatTiming(string name, TimeSpan duration)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{name};dur={duration.TotalMilliseconds:F3}");
    }
}
