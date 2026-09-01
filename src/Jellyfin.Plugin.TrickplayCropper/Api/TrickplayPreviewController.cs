using System.Globalization;
using Jellyfin.Plugin.TrickplayCropper.Caching;
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
    private readonly ITrickplayPreview trickplayPreview;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayPreviewController"/> class.
    /// </summary>
    /// <param name="trickplayPreview">The Trickplay Preview request module.</param>
    public TrickplayPreviewController(ITrickplayPreview trickplayPreview)
    {
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
        Guid itemId,
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
        PreviewCacheDisposition disposition = outcome.Telemetry.CacheDisposition
            ?? throw new InvalidOperationException("A successful Preview response requires a cache disposition.");
        Response.Headers["X-Trickplay-Cache"] = disposition.ToString().ToUpperInvariant();
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
