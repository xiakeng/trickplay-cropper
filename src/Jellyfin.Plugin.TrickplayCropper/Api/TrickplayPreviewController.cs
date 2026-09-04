using System.Diagnostics.CodeAnalysis;
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
[Authorize]
[Route("TrickplayCropper/Videos/{itemId}/Preview")]
public sealed class TrickplayPreviewController : ControllerBase
{
    private const string FrameIndexHeaderName = "X-Trickplay-Frame-Index";

    private readonly ITrickplayFrameProbe frameProbe;
    private readonly ITrickplayPreview trickplayPreview;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayPreviewController"/> class.
    /// </summary>
    /// <param name="frameProbe">The Trickplay Frame Probe module.</param>
    /// <param name="trickplayPreview">The Trickplay Preview request module.</param>
    public TrickplayPreviewController(ITrickplayFrameProbe frameProbe, ITrickplayPreview trickplayPreview)
    {
        this.frameProbe = frameProbe;
        this.trickplayPreview = trickplayPreview;
    }

    /// <summary>
    /// Gets one Trickplay Preview for the requested playback position.
    /// </summary>
    /// <param name="itemId">The logical video identifier.</param>
    /// <param name="parameters">The normalized query-string parameters.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The mapped HTTP response.</returns>
    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromRoute] Guid itemId,
        [FromQuery] PreviewQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        var query = new PreviewQuery(itemId, parameters.MediaSourceId, parameters.PositionTicks);
        EntityTagHeaderValue[] conditionalEntityTags = Request.GetTypedHeaders().IfNoneMatch?.ToArray() ?? [];
        PreviewOutcome outcome = await trickplayPreview.GetAsync(
            query,
            User,
            conditionalEntityTags,
            cancellationToken).ConfigureAwait(false);

        return MapOutcome(outcome);
    }

    /// <summary>
    /// Probes the Frame Index the requested playback position selects, without a response body.
    /// </summary>
    /// <param name="itemId">The raw logical video identifier.</param>
    /// <param name="mediaSourceId">The raw optional alternate media source identifier.</param>
    /// <param name="positionTicks">The raw playback position in Jellyfin ticks.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The mapped bodyless HTTP response.</returns>
    [HttpHead]
    public async Task<IActionResult> HeadAsync(
        [FromRoute] string? itemId,
        [FromQuery] string? mediaSourceId,
        [FromQuery] string? positionTicks,
        CancellationToken cancellationToken)
    {
        if (!TryCreateQuery(itemId, mediaSourceId, positionTicks, out PreviewQuery? query))
        {
            return CreateBodylessResult(StatusCodes.Status400BadRequest);
        }

        FrameProbeOutcome outcome = await frameProbe.ProbeAsync(query, User, cancellationToken)
            .ConfigureAwait(false);
        return MapProbeOutcome(outcome);
    }

    private static bool TryCreateQuery(
        string? itemId,
        string? mediaSourceId,
        string? positionTicks,
        [NotNullWhen(true)] out PreviewQuery? query)
    {
        query = null;
        if (!Guid.TryParse(itemId, out Guid parsedItemId))
        {
            return false;
        }

        Guid? parsedMediaSourceId = null;
        if (!string.IsNullOrEmpty(mediaSourceId))
        {
            if (!Guid.TryParse(mediaSourceId, out Guid parsedSourceId))
            {
                return false;
            }

            parsedMediaSourceId = parsedSourceId;
        }

        if (!long.TryParse(
            positionTicks,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long parsedPositionTicks))
        {
            return false;
        }

        query = new PreviewQuery(parsedItemId, parsedMediaSourceId, parsedPositionTicks);
        return true;
    }

    private EmptyResult MapProbeOutcome(FrameProbeOutcome outcome)
    {
        return outcome switch
        {
            FrameProbeOutcome.Success success => CreateFrameIndexResult(success.FrameIndex),
            FrameProbeOutcome.BadRequest => CreateBodylessResult(StatusCodes.Status400BadRequest),
            FrameProbeOutcome.Unauthorized => CreateBodylessResult(StatusCodes.Status401Unauthorized),
            FrameProbeOutcome.Forbidden => CreateBodylessResult(StatusCodes.Status403Forbidden),
            FrameProbeOutcome.NotFound => CreateBodylessResult(StatusCodes.Status404NotFound),
            FrameProbeOutcome.InternalError => CreateBodylessResult(StatusCodes.Status500InternalServerError),
            _ => throw new InvalidOperationException($"Unknown frame probe outcome {outcome.GetType().Name}."),
        };
    }

    private EmptyResult CreateFrameIndexResult(int frameIndex)
    {
        Response.Headers[FrameIndexHeaderName] = frameIndex.ToString(CultureInfo.InvariantCulture);
        Response.Headers.CacheControl = "private, no-cache";
        return CreateBodylessResult(StatusCodes.Status200OK);
    }

    // Setting the status directly and returning an empty result keeps every HEAD outcome bodyless,
    // because the [ApiController] client-error transform would otherwise add a ProblemDetails body.
    private EmptyResult CreateBodylessResult(int statusCode)
    {
        Response.StatusCode = statusCode;
        return new EmptyResult();
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
        Response.Headers.CacheControl = "private, no-cache";
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
