using System.Collections;
using System.Diagnostics;
using System.Globalization;
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
    private readonly IPreviewContextResolver contextResolver;
    private readonly IPreviewSourceResolver sourceResolver;
    private readonly IPreviewCache previewCache;
    private readonly ITrickplayPreviewEncoder encoder;
    private readonly ILogger<TrickplayPreview> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayPreview"/> class.
    /// </summary>
    public TrickplayPreview(
        IPreviewContextResolver contextResolver,
        IPreviewSourceResolver sourceResolver,
        IPreviewCache previewCache,
        ITrickplayPreviewEncoder encoder,
        ILogger<TrickplayPreview> logger)
    {
        this.contextResolver = contextResolver;
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
        long requestStarted = Stopwatch.GetTimestamp();
        var failureContext = new RequestFailureContext(query);
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
            LogFailure(exception, failureContext, Stopwatch.GetElapsedTime(requestStarted));
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
        PreviewContextResolution contextResolution = await contextResolver
            .ResolveAsync(query, user, cancellationToken)
            .ConfigureAwait(false);
        if (contextResolution is not PreviewContextResolution.Resolved resolved)
        {
            return MapContextFailure(contextResolution);
        }

        PreviewSourceResolution sourceResolution = await sourceResolver
            .ResolveAsync(resolved.Context)
            .ConfigureAwait(false);
        TimeSpan lookupDuration = Stopwatch.GetElapsedTime(lookupStarted);
        return sourceResolution switch
        {
            PreviewSourceResolution.Found found => await GetResolvedAsync(
                found.Source,
                lookupDuration,
                conditionalEntityTags,
                failureContext,
                cancellationToken).ConfigureAwait(false),
            PreviewSourceResolution.NotFound => new PreviewOutcome.NotFound(),
            _ => throw new InvalidOperationException(
                $"Unknown source resolution {sourceResolution.GetType().Name}."),
        };
    }

    private static PreviewOutcome MapContextFailure(PreviewContextResolution contextResolution)
    {
        return contextResolution switch
        {
            PreviewContextResolution.BadRequest => new PreviewOutcome.BadRequest(),
            PreviewContextResolution.Unauthorized => new PreviewOutcome.Unauthorized(),
            PreviewContextResolution.Forbidden => new PreviewOutcome.Forbidden(),
            PreviewContextResolution.NotFound => new PreviewOutcome.NotFound(),
            _ => throw new InvalidOperationException(
                $"Unknown preview context resolution {contextResolution.GetType().Name}."),
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
        if (MatchesConditionalEntityTag(identity.EntityTag, conditionalEntityTags))
        {
            var notModifiedTelemetry = new PreviewTelemetry.Conditional(lookupDuration);
            return new PreviewOutcome.NotModified(identity.EntityTag, notModifiedTelemetry);
        }

        long cacheStarted = Stopwatch.GetTimestamp();
        PreviewCacheResult cacheResult = await previewCache.GetOrCreateAsync(
            identity,
            (destination, token) => encoder.EncodeAsync(source, destination, token),
            cancellationToken).ConfigureAwait(false);
        TimeSpan cacheDuration = Stopwatch.GetElapsedTime(cacheStarted);
        var telemetry = new PreviewTelemetry.CacheAccess(lookupDuration, cacheDuration, cacheResult);
        return new PreviewOutcome.Ok(cacheResult.Content, identity.EntityTag, telemetry);
    }

    private static bool MatchesConditionalEntityTag(
        string entityTag,
        IReadOnlyCollection<EntityTagHeaderValue> conditionalEntityTags)
    {
        var currentEntityTag = new EntityTagHeaderValue(entityTag);
        return conditionalEntityTags.Any(
            candidate => candidate.Equals(EntityTagHeaderValue.Any)
                || currentEntityTag.Compare(candidate, useStrongComparison: false));
    }

    private void LogFailure(
        Exception exception,
        RequestFailureContext failureContext,
        TimeSpan elapsed)
    {
        failureContext.Capture(exception);
        PreviewFailureLog failureLog = failureContext.CreateLog(elapsed.TotalMilliseconds);
        logger.Log(
            LogLevel.Error,
            PreviewFailureLog.Event,
            failureLog,
            null,
            static (state, _) => state.Message);
    }

    private sealed class RequestFailureContext
    {
        private readonly PreviewQuery query;

        public RequestFailureContext(PreviewQuery query)
        {
            this.query = query;
        }

        public int? ActualHeight { get; private set; }

        public int? ActualWidth { get; private set; }

        public int? Column { get; private set; }

        public int? CropHeight { get; private set; }

        public int? CropWidth { get; private set; }

        public long? CropX { get; private set; }

        public long? CropY { get; private set; }

        public string? DecodePath { get; private set; }

        public string ExceptionType { get; private set; } = nameof(Exception);

        public string? FailedValidation { get; private set; }

        public long? FailedValue { get; private set; }

        public int? FrameHeight { get; private set; }

        public int? FrameIndex { get; private set; }

        public int? FrameWidth { get; private set; }

        public int? IntervalMilliseconds { get; private set; }

        public int? Row { get; private set; }

        public string? SkiaResult { get; private set; }

        public long? SourceLastWriteUtcTicks { get; private set; }

        public long? SourceLength { get; private set; }

        public int? SpriteIndex { get; private set; }

        public int? ThumbnailCount { get; private set; }

        public int? TileHeight { get; private set; }

        public int? TileWidth { get; private set; }

        public void Capture(ResolvedPreviewSource source)
        {
            Capture(source.Metadata);
            Capture(source.Selection);
            SourceLength = source.SourceLength;
            SourceLastWriteUtcTicks = source.SourceLastWriteUtcTicks;
        }

        public void Capture(Exception exception)
        {
            ExceptionType = exception.GetType().Name;
            switch (exception)
            {
                case InvalidTrickplayMetadataException invalidMetadata:
                    Capture(invalidMetadata.Metadata);
                    if (invalidMetadata.SelectionDiagnostics is not null)
                    {
                        Capture(invalidMetadata.SelectionDiagnostics);
                    }

                    FailedValidation = invalidMetadata.FailedValidation;
                    FailedValue = invalidMetadata.FailedValue;
                    break;
                case PreviewStageException stageException:
                    ExceptionType = stageException.CauseType;
                    Capture(stageException.Details);
                    break;
                default:
                    break;
            }
        }

        public PreviewFailureLog CreateLog(double elapsedMilliseconds)
        {
            string message = string.Create(
                CultureInfo.InvariantCulture,
                $"Trickplay Preview request failed with {ExceptionType} for ItemId {query.ItemId}, "
                + $"MediaSourceId {query.ResolvedMediaSourceId}, PositionTicks {query.PositionTicks}, "
                + $"ElapsedMilliseconds {elapsedMilliseconds}.");
            KeyValuePair<string, object?>[] properties =
            [
                new("ItemId", query.ItemId),
                new("MediaSourceId", query.ResolvedMediaSourceId),
                new("PositionTicks", query.PositionTicks),
                new("FrameWidth", FrameWidth),
                new("FrameHeight", FrameHeight),
                new("IntervalMilliseconds", IntervalMilliseconds),
                new("TileWidth", TileWidth),
                new("TileHeight", TileHeight),
                new("ThumbnailCount", ThumbnailCount),
                new("FrameIndex", FrameIndex),
                new("SpriteIndex", SpriteIndex),
                new("Row", Row),
                new("Column", Column),
                new("CropX", CropX),
                new("CropY", CropY),
                new("CropWidth", CropWidth),
                new("CropHeight", CropHeight),
                new("SourceLength", SourceLength),
                new("SourceLastWriteUtcTicks", SourceLastWriteUtcTicks),
                new("ActualWidth", ActualWidth),
                new("ActualHeight", ActualHeight),
                new("DecodePath", DecodePath),
                new("SkiaResult", SkiaResult),
                new("ExceptionType", ExceptionType),
                new("FailedValidation", FailedValidation),
                new("FailedValue", FailedValue),
                new("ElapsedMilliseconds", elapsedMilliseconds),
                new("{OriginalFormat}", PreviewFailureLog.MessageTemplate),
            ];
            return new PreviewFailureLog(message, properties);
        }

        private void Capture(PreviewFailureDetails details)
        {
            if (details.Metadata is not null)
            {
                Capture(details.Metadata);
            }

            if (details.Selection is not null)
            {
                Capture(details.Selection);
            }

            ActualHeight = details.ActualHeight ?? ActualHeight;
            ActualWidth = details.ActualWidth ?? ActualWidth;
            DecodePath = details.DecodePath ?? DecodePath;
            FailedValidation = details.FailedValidation ?? FailedValidation;
            FailedValue = details.FailedValue ?? FailedValue;
            SkiaResult = details.SkiaResult ?? SkiaResult;
            SourceLastWriteUtcTicks = details.SourceLastWriteUtcTicks ?? SourceLastWriteUtcTicks;
            SourceLength = details.SourceLength ?? SourceLength;
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

        private void Capture(FrameSelection selection)
        {
            FrameIndex = selection.FrameIndex;
            SpriteIndex = selection.SpriteIndex;
            Row = selection.Row;
            Column = selection.Column;
            CropX = selection.CropX;
            CropY = selection.CropY;
            CropWidth = selection.CropWidth;
            CropHeight = selection.CropHeight;
        }

        private void Capture(FrameSelectionDiagnostics diagnostics)
        {
            FrameIndex = ConvertToInt32(diagnostics.FrameIndex);
            SpriteIndex = ConvertToInt32(diagnostics.SpriteIndex);
            Row = ConvertToInt32(diagnostics.Row);
            Column = ConvertToInt32(diagnostics.Column);
            CropX = diagnostics.CropX;
            CropY = diagnostics.CropY;
            CropWidth = diagnostics.CropWidth;
            CropHeight = diagnostics.CropHeight;
        }

        private static int? ConvertToInt32(long value)
        {
            return value is >= int.MinValue and <= int.MaxValue ? (int)value : null;
        }
    }

    private sealed class PreviewFailureLog : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public const string MessageTemplate =
            "Trickplay Preview request failed with {ExceptionType} for ItemId {ItemId}, "
            + "MediaSourceId {MediaSourceId}, PositionTicks {PositionTicks}, "
            + "ElapsedMilliseconds {ElapsedMilliseconds}.";

        private readonly KeyValuePair<string, object?>[] properties;

        public PreviewFailureLog(string message, KeyValuePair<string, object?>[] properties)
        {
            Message = message;
            this.properties = properties;
        }

        public static EventId Event { get; } = new(1000, "TrickplayPreviewRequestFailed");

        public int Count => properties.Length;

        public KeyValuePair<string, object?> this[int index] => properties[index];

        public string Message { get; }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<string, object?>>)properties).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return properties.GetEnumerator();
        }

        public override string ToString()
        {
            return Message;
        }
    }
}
