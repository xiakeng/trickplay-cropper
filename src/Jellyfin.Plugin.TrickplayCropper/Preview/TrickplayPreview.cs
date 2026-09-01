using System.Diagnostics;
using System.Security.Claims;
using Jellyfin.Plugin.TrickplayCropper.Caching;
using Jellyfin.Plugin.TrickplayCropper.Imaging;
using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Owns the ordered workflow for one Trickplay Preview request.
/// </summary>
internal sealed class TrickplayPreview : ITrickplayPreview
{
    private static readonly Action<ILogger, Guid, Guid, long, double, Exception?> logRequestFailure =
        LoggerMessage.Define<Guid, Guid, long, double>(
            LogLevel.Error,
            new EventId(1000, "TrickplayPreviewRequestFailed"),
            "Trickplay Preview request failed for ItemId {ItemId}, MediaSourceId {MediaSourceId}, "
            + "PositionTicks {PositionTicks}, ElapsedMilliseconds {ElapsedMilliseconds}");

    private readonly IPreviewSourceResolver sourceResolver;
    private readonly IPreviewCache previewCache;
    private readonly ITrickplayPreviewEncoder encoder;
    private readonly ILogger<TrickplayPreview> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayPreview"/> class.
    /// </summary>
    public TrickplayPreview(
        IPreviewSourceResolver sourceResolver,
        IPreviewCache previewCache,
        ITrickplayPreviewEncoder encoder,
        ILogger<TrickplayPreview> logger)
    {
        this.sourceResolver = sourceResolver;
        this.previewCache = previewCache;
        this.encoder = encoder;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<PreviewOutcome> GetAsync(
        PreviewQuery query,
        ClaimsPrincipal user,
        IReadOnlyCollection<EntityTagHeaderValue> conditionalEntityTags,
        CancellationToken cancellationToken)
    {
        if (query.PositionTicks < 0)
        {
            return new PreviewOutcome.BadRequest();
        }

        long requestStarted = Stopwatch.GetTimestamp();
        try
        {
            return await ProcessAsync(query, user, conditionalEntityTags, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(exception, query, Stopwatch.GetElapsedTime(requestStarted));
            return new PreviewOutcome.InternalError();
        }
    }

    private async Task<PreviewOutcome> ProcessAsync(
        PreviewQuery query,
        ClaimsPrincipal user,
        IReadOnlyCollection<EntityTagHeaderValue> conditionalEntityTags,
        CancellationToken cancellationToken)
    {
        long lookupStarted = Stopwatch.GetTimestamp();
        PreviewSourceResolution resolution = await sourceResolver
            .ResolveAsync(query, user, cancellationToken)
            .ConfigureAwait(false);
        TimeSpan lookupDuration = Stopwatch.GetElapsedTime(lookupStarted);
        return resolution switch
        {
            PreviewSourceResolution.Found found => await GetResolvedAsync(
                found.Source,
                lookupDuration,
                conditionalEntityTags,
                cancellationToken).ConfigureAwait(false),
            PreviewSourceResolution.Unauthorized => new PreviewOutcome.Unauthorized(),
            PreviewSourceResolution.Forbidden => new PreviewOutcome.Forbidden(),
            PreviewSourceResolution.NotFound => new PreviewOutcome.NotFound(),
            _ => throw new InvalidOperationException($"Unknown source resolution {resolution.GetType().Name}."),
        };
    }

    private async Task<PreviewOutcome> GetResolvedAsync(
        ResolvedPreviewSource source,
        TimeSpan lookupDuration,
        IReadOnlyCollection<EntityTagHeaderValue> conditionalEntityTags,
        CancellationToken cancellationToken)
    {
        PreviewIdentity identity = PreviewIdentity.Create(source);
        _ = conditionalEntityTags;
        long cacheStarted = Stopwatch.GetTimestamp();
        PreviewCacheResult cacheResult = await previewCache.GetOrCreateAsync(
            identity,
            (destination, token) => encoder.EncodeAsync(source, destination, token),
            cancellationToken).ConfigureAwait(false);
        TimeSpan cacheDuration = Stopwatch.GetElapsedTime(cacheStarted);
        PreviewTelemetry telemetry = CreateTelemetry(lookupDuration, cacheDuration, cacheResult);
        return new PreviewOutcome.Ok(cacheResult.Content, identity.EntityTag, telemetry);
    }

    private static PreviewTelemetry CreateTelemetry(
        TimeSpan lookupDuration,
        TimeSpan cacheDuration,
        PreviewCacheResult cacheResult)
    {
        return new PreviewTelemetry(
            lookupDuration,
            cacheDuration,
            cacheResult.EncodingTelemetry?.Decode,
            cacheResult.EncodingTelemetry?.Encode,
            cacheResult.Disposition);
    }

    private void LogFailure(Exception exception, PreviewQuery query, TimeSpan elapsed)
    {
        logRequestFailure(
            logger,
            query.ItemId,
            query.ResolvedMediaSourceId,
            query.PositionTicks,
            elapsed.TotalMilliseconds,
            exception);
    }
}
