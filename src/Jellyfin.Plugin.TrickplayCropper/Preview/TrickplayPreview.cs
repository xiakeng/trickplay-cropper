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
internal sealed partial class TrickplayPreview : ITrickplayPreview
{
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
        var failureContext = new RequestFailureContext();
        try
        {
            return await ProcessAsync(
                query,
                user,
                conditionalEntityTags,
                failureContext,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(exception, query, failureContext, Stopwatch.GetElapsedTime(requestStarted));
            return new PreviewOutcome.InternalError();
        }
    }

    private async Task<PreviewOutcome> ProcessAsync(
        PreviewQuery query,
        ClaimsPrincipal user,
        IReadOnlyCollection<EntityTagHeaderValue> conditionalEntityTags,
        RequestFailureContext failureContext,
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
                failureContext,
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
        RequestFailureContext failureContext,
        CancellationToken cancellationToken)
    {
        failureContext.Capture(source);
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

    private void LogFailure(
        Exception exception,
        PreviewQuery query,
        RequestFailureContext failureContext,
        TimeSpan elapsed)
    {
        failureContext.Capture(exception);
        LogRequestFailure(
            logger,
            query.ItemId,
            query.ResolvedMediaSourceId,
            query.PositionTicks,
            failureContext.FrameWidth,
            failureContext.FrameHeight,
            failureContext.IntervalMilliseconds,
            failureContext.TileWidth,
            failureContext.TileHeight,
            failureContext.ThumbnailCount,
            failureContext.FrameIndex,
            failureContext.SpriteIndex,
            failureContext.Row,
            failureContext.Column,
            failureContext.CropX,
            failureContext.CropY,
            failureContext.CropWidth,
            failureContext.CropHeight,
            failureContext.SourceLength,
            failureContext.SourceLastWriteUtcTicks,
            exception.GetType().Name,
            failureContext.FailedValidation,
            failureContext.FailedValue,
            elapsed.TotalMilliseconds);
    }

    [LoggerMessage(
        EventId = 1000,
        EventName = "TrickplayPreviewRequestFailed",
        Level = LogLevel.Error,
        Message = "Trickplay Preview request failed for ItemId {ItemId}, MediaSourceId {MediaSourceId}, "
            + "PositionTicks {PositionTicks}, FrameWidth {FrameWidth}, FrameHeight {FrameHeight}, "
            + "IntervalMilliseconds {IntervalMilliseconds}, TileWidth {TileWidth}, TileHeight {TileHeight}, "
            + "ThumbnailCount {ThumbnailCount}, FrameIndex {FrameIndex}, SpriteIndex {SpriteIndex}, Row {Row}, "
            + "Column {Column}, CropX {CropX}, CropY {CropY}, CropWidth {CropWidth}, CropHeight {CropHeight}, "
            + "SourceLength {SourceLength}, SourceLastWriteUtcTicks {SourceLastWriteUtcTicks}, "
            + "ExceptionType {ExceptionType}, FailedValidation {FailedValidation}, FailedValue {FailedValue}, "
            + "ElapsedMilliseconds {ElapsedMilliseconds}")]
    private static partial void LogRequestFailure(
        ILogger logger,
        Guid itemId,
        Guid mediaSourceId,
        long positionTicks,
        int? frameWidth,
        int? frameHeight,
        int? intervalMilliseconds,
        int? tileWidth,
        int? tileHeight,
        int? thumbnailCount,
        int? frameIndex,
        int? spriteIndex,
        int? row,
        int? column,
        int? cropX,
        int? cropY,
        int? cropWidth,
        int? cropHeight,
        long? sourceLength,
        long? sourceLastWriteUtcTicks,
        string exceptionType,
        string? failedValidation,
        long? failedValue,
        double elapsedMilliseconds);

    private sealed class RequestFailureContext
    {
        public int? Column { get; private set; }

        public int? CropHeight { get; private set; }

        public int? CropWidth { get; private set; }

        public int? CropX { get; private set; }

        public int? CropY { get; private set; }

        public string? FailedValidation { get; private set; }

        public long? FailedValue { get; private set; }

        public int? FrameHeight { get; private set; }

        public int? FrameIndex { get; private set; }

        public int? FrameWidth { get; private set; }

        public int? IntervalMilliseconds { get; private set; }

        public int? Row { get; private set; }

        public long? SourceLastWriteUtcTicks { get; private set; }

        public long? SourceLength { get; private set; }

        public int? SpriteIndex { get; private set; }

        public int? ThumbnailCount { get; private set; }

        public int? TileHeight { get; private set; }

        public int? TileWidth { get; private set; }

        public void Capture(ResolvedPreviewSource source)
        {
            Capture(source.Metadata);
            FrameIndex = source.Selection.FrameIndex;
            SpriteIndex = source.Selection.SpriteIndex;
            Row = source.Selection.Row;
            Column = source.Selection.Column;
            CropX = source.Selection.CropX;
            CropY = source.Selection.CropY;
            CropWidth = source.Selection.CropWidth;
            CropHeight = source.Selection.CropHeight;
            SourceLength = source.SourceLength;
            SourceLastWriteUtcTicks = source.SourceLastWriteUtcTicks;
        }

        public void Capture(Exception exception)
        {
            if (exception is InvalidTrickplayMetadataException invalidMetadata)
            {
                Capture(invalidMetadata.Metadata);
                FailedValidation = invalidMetadata.FailedValidation;
                FailedValue = invalidMetadata.FailedValue;
            }
        }

        private void Capture(TrickplayMetadata metadata)
        {
            FrameWidth = metadata.FrameWidth;
            FrameHeight = metadata.FrameHeight;
            IntervalMilliseconds = metadata.IntervalMilliseconds;
            TileWidth = metadata.TileWidth;
            TileHeight = metadata.TileHeight;
            ThumbnailCount = metadata.ThumbnailCount;
        }
    }
}
