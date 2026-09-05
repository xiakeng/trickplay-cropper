using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Trickplay;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Owns immutable generated-metadata observations shared by Trickplay Frame Probes and Preview requests.
/// </summary>
internal sealed class TrickplayMetadataCache
{
    private static readonly TimeSpan negativeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan positiveLifetime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, SourceState> sources = new();
    private readonly TimeProvider timeProvider;
    private readonly ITrickplayManager trickplayManager;
    private long nextObservationSequence;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayMetadataCache"/> class.
    /// </summary>
    /// <param name="trickplayManager">Reads authoritative generated Trickplay metadata.</param>
    /// <param name="timeProvider">Provides authoritative read-start time.</param>
    public TrickplayMetadataCache(ITrickplayManager trickplayManager, TimeProvider timeProvider)
    {
        this.trickplayManager = trickplayManager;
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// Reuses a current observation for a Trickplay Frame Probe or reads an authoritative replacement.
    /// </summary>
    public Task<TrickplayMetadataResolution> GetForProbeAsync(
        Guid sourceVideoId,
        int selectedResolution,
        CancellationToken cancellationToken)
    {
        var request = new MetadataRequest(sourceVideoId, selectedResolution, MetadataAccess.Probe);
        return GetAsync(request, cancellationToken);
    }

    /// <summary>
    /// Reads current authoritative metadata for a Trickplay Preview request unless current absence applies.
    /// </summary>
    public Task<TrickplayMetadataResolution> GetForPreviewAsync(
        Guid sourceVideoId,
        int selectedResolution,
        CancellationToken cancellationToken)
    {
        var request = new MetadataRequest(sourceVideoId, selectedResolution, MetadataAccess.Preview);
        return GetAsync(request, cancellationToken);
    }

    private async Task<TrickplayMetadataResolution> GetAsync(
        MetadataRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset readStartedAt = timeProvider.GetUtcNow();
        long sequence = Interlocked.Increment(ref nextObservationSequence);
        SourceState source = GetSourceForRequest(request, readStartedAt, sequence, out var cached);
        if (cached is not null)
        {
            return cached;
        }

        Task<ReadOutcome> read = ReadAndPublishAsync(source, request, readStartedAt, sequence);
        ReadOutcome outcome = await read.WaitAsync(cancellationToken).ConfigureAwait(false);
        return outcome switch
        {
            ReadOutcome.Succeeded succeeded => succeeded.Resolution,
            ReadOutcome.Failed failed => ThrowFailure(failed),
            _ => throw new InvalidOperationException($"Unknown metadata read outcome {outcome.GetType().Name}."),
        };
    }

    private SourceState GetSourceForRequest(
        MetadataRequest request,
        DateTimeOffset now,
        long sequence,
        out TrickplayMetadataResolution? cached)
    {
        while (true)
        {
            SourceState source = sources.GetOrAdd(request.SourceVideoId, static _ => new SourceState());
            if (source.TryResolveOrRegister(request, now, sequence, out cached))
            {
                return source;
            }

            sources.TryRemove(new KeyValuePair<Guid, SourceState>(request.SourceVideoId, source));
        }
    }

    private async Task<ReadOutcome> ReadAndPublishAsync(
        SourceState source,
        MetadataRequest request,
        DateTimeOffset readStartedAt,
        long sequence)
    {
        try
        {
            Dictionary<int, TrickplayInfo> resolutions = await trickplayManager
                .GetTrickplayResolutions(request.SourceVideoId)
                .ConfigureAwait(false);
            MetadataObservation observation = CreateObservation(request.SelectedResolution, resolutions);
            DateTimeOffset completedAt = timeProvider.GetUtcNow();
            if (!IsCurrent(observation.Resolution, readStartedAt, completedAt))
            {
                source.Reject(request.SelectedResolution, sequence);
                return ReadOutcome.Capture(
                    new InvalidOperationException(
                        "The generated Trickplay metadata observation expired before its read completed."));
            }

            source.Publish(observation, request.SelectedResolution, readStartedAt, sequence);
            return new ReadOutcome.Succeeded(observation.Resolution);
        }
        catch (InvalidTrickplayMetadataException failure)
        {
            source.Reject(request.SelectedResolution, sequence);
            return ReadOutcome.Capture(failure);
        }
        catch (Exception failure)
        {
            return ReadOutcome.Capture(failure);
        }
        finally
        {
            if (source.Complete(sequence, timeProvider.GetUtcNow()))
            {
                sources.TryRemove(new KeyValuePair<Guid, SourceState>(request.SourceVideoId, source));
            }
        }
    }

    private static MetadataObservation CreateObservation(
        int selectedResolution,
        IReadOnlyDictionary<int, TrickplayInfo> resolutions)
    {
        Dictionary<int, TrickplayMetadata> snapshot = CopyMetadata(resolutions);
        TrickplayMetadataResolution resolution = Resolve(selectedResolution, snapshot);
        return new MetadataObservation(snapshot, resolution);
    }

    private static Dictionary<int, TrickplayMetadata> CopyMetadata(
        IReadOnlyDictionary<int, TrickplayInfo> resolutions)
    {
        return resolutions.ToDictionary(
            pair => pair.Key,
            pair => new TrickplayMetadata(
                pair.Value.Width,
                pair.Value.Height,
                pair.Value.Interval,
                pair.Value.TileWidth,
                pair.Value.TileHeight,
                pair.Value.ThumbnailCount));
    }

    private static TrickplayMetadataResolution Resolve(
        int selectedResolution,
        IReadOnlyDictionary<int, TrickplayMetadata> resolutions)
    {
        if (resolutions.Count == 0)
        {
            return new TrickplayMetadataResolution.NotFound(PreviewUnavailableReason.NoGeneratedMetadata);
        }

        if (!resolutions.TryGetValue(selectedResolution, out TrickplayMetadata? metadata))
        {
            return new TrickplayMetadataResolution.NotFound(PreviewUnavailableReason.SelectedResolutionMissing);
        }

        if (metadata.ThumbnailCount <= 0)
        {
            return new TrickplayMetadataResolution.NotFound(PreviewUnavailableReason.NoThumbnails);
        }

        ValidateMetadata(metadata, selectedResolution, resolutions.Keys);
        return new TrickplayMetadataResolution.Available(metadata);
    }

    private static void ValidateMetadata(
        TrickplayMetadata metadata,
        int selectedResolution,
        IEnumerable<int> generatedKeys)
    {
        try
        {
            metadata.Validate();
            if (metadata.FrameWidth != selectedResolution)
            {
                throw new InvalidTrickplayMetadataException(
                    metadata,
                    "FrameWidthMatchesResolutionKey",
                    metadata.FrameWidth);
            }
        }
        catch (InvalidTrickplayMetadataException failure)
        {
            failure.Configuration = new PreviewConfigurationDiagnostics
            {
                GeneratedKeys = generatedKeys.Order().ToArray(),
            };
            throw;
        }
    }

    private static bool IsCurrent(
        TrickplayMetadataResolution resolution,
        DateTimeOffset readStartedAt,
        DateTimeOffset now)
    {
        TimeSpan age = now - readStartedAt;
        return age >= TimeSpan.Zero && age < GetLifetime(resolution);
    }

    private static TimeSpan GetLifetime(TrickplayMetadataResolution resolution)
    {
        return resolution is TrickplayMetadataResolution.Available
            ? positiveLifetime
            : negativeLifetime;
    }

    private static TrickplayMetadataResolution? CreateReusableResolution(
        int resolutionKey,
        TrickplayMetadata metadata)
    {
        if (metadata.ThumbnailCount <= 0)
        {
            return new TrickplayMetadataResolution.NotFound(PreviewUnavailableReason.NoThumbnails);
        }

        try
        {
            metadata.Validate();
            return metadata.FrameWidth == resolutionKey
                ? new TrickplayMetadataResolution.Available(metadata)
                : null;
        }
        catch (InvalidTrickplayMetadataException)
        {
            return null;
        }
    }

    private static TrickplayMetadataResolution ThrowFailure(ReadOutcome.Failed failure)
    {
        failure.Failure.Throw();
        throw new InvalidOperationException("The captured metadata failure did not throw.");
    }

    private enum MetadataAccess
    {
        Probe,
        Preview,
    }

    private sealed record MetadataObservation(
        IReadOnlyDictionary<int, TrickplayMetadata> Resolutions,
        TrickplayMetadataResolution Resolution);

    private sealed record MetadataRequest(
        Guid SourceVideoId,
        int SelectedResolution,
        MetadataAccess Access);

    private abstract record ReadOutcome
    {
        public static Failed Capture(Exception failure)
        {
            return new Failed(ExceptionDispatchInfo.Capture(failure));
        }

        internal sealed record Succeeded(TrickplayMetadataResolution Resolution) : ReadOutcome;

        internal sealed record Failed(ExceptionDispatchInfo Failure) : ReadOutcome;
    }

    private sealed class SourceState
    {
        private readonly HashSet<long> activeReads = [];
        private readonly object gate = new();
        private readonly Dictionary<int, OrderedResolution> resolutions = [];
        private Coverage? coverage;
        private bool retired;

        public bool TryResolveOrRegister(
            MetadataRequest request,
            DateTimeOffset now,
            long sequence,
            out TrickplayMetadataResolution? resolution)
        {
            lock (gate)
            {
                resolution = null;
                if (retired)
                {
                    return false;
                }

                Prune(now);
                OrderedResolution? current = FindCurrent(request.SelectedResolution);
                if (CanReuse(current, request.Access, now))
                {
                    resolution = current!.Resolution;
                    return true;
                }

                activeReads.Add(sequence);
                return true;
            }
        }

        public void Publish(
            MetadataObservation observation,
            int selectedResolution,
            DateTimeOffset readStartedAt,
            long sequence)
        {
            lock (gate)
            {
                if (coverage is not null && coverage.Sequence > sequence)
                {
                    return;
                }

                PublishObservedRows(observation.Resolutions, readStartedAt, sequence);
                TombstoneMissingRows(observation.Resolutions.Keys, sequence);
                coverage = new Coverage(observation.Resolutions.Keys.ToArray(), readStartedAt, sequence);
                if (!resolutions.TryGetValue(selectedResolution, out OrderedResolution? current)
                    || current.Sequence < sequence)
                {
                    resolutions[selectedResolution] = new OrderedResolution(
                        observation.Resolution,
                        readStartedAt,
                        sequence);
                }
            }
        }

        public void Reject(int selectedResolution, long sequence)
        {
            lock (gate)
            {
                if (!resolutions.TryGetValue(selectedResolution, out OrderedResolution? current)
                    || current.Sequence < sequence)
                {
                    resolutions[selectedResolution] = new OrderedResolution(null, null, sequence);
                }
            }
        }

        public bool Complete(long sequence, DateTimeOffset now)
        {
            lock (gate)
            {
                activeReads.Remove(sequence);
                Prune(now);
                if (activeReads.Count == 0 && coverage is null && resolutions.Count == 0)
                {
                    retired = true;
                }

                return retired;
            }
        }

        private OrderedResolution? FindCurrent(int selectedResolution)
        {
            resolutions.TryGetValue(selectedResolution, out OrderedResolution? selected);
            OrderedResolution? derived = DeriveAbsence(selectedResolution);
            if (selected is null)
            {
                return derived;
            }

            bool derivedSupersedes = derived is not null
                && (derived.Sequence > selected.Sequence
                    || (derived.Sequence == selected.Sequence && selected.Resolution is null));
            return derivedSupersedes ? derived : selected;
        }

        private OrderedResolution? DeriveAbsence(int selectedResolution)
        {
            if (coverage is null || coverage.GeneratedKeys.Contains(selectedResolution))
            {
                return null;
            }

            PreviewUnavailableReason reason = coverage.GeneratedKeys.Length == 0
                ? PreviewUnavailableReason.NoGeneratedMetadata
                : PreviewUnavailableReason.SelectedResolutionMissing;
            return new OrderedResolution(
                new TrickplayMetadataResolution.NotFound(reason),
                coverage.ReadStartedAt,
                coverage.Sequence);
        }

        private static bool CanReuse(
            OrderedResolution? current,
            MetadataAccess access,
            DateTimeOffset now)
        {
            if (current?.Resolution is null || current.ReadStartedAt is null)
            {
                return false;
            }

            if (access == MetadataAccess.Preview
                && current.Resolution is TrickplayMetadataResolution.Available)
            {
                return false;
            }

            return IsCurrent(current.Resolution, current.ReadStartedAt.Value, now);
        }

        private void PublishObservedRows(
            IReadOnlyDictionary<int, TrickplayMetadata> observed,
            DateTimeOffset readStartedAt,
            long sequence)
        {
            foreach ((int width, TrickplayMetadata metadata) in observed)
            {
                resolutions.TryGetValue(width, out OrderedResolution? current);
                if (current is not null && current.Sequence >= sequence)
                {
                    continue;
                }

                TrickplayMetadataResolution? reusable = CreateReusableResolution(width, metadata);
                resolutions[width] = new OrderedResolution(
                    reusable,
                    reusable is null ? null : readStartedAt,
                    sequence);
            }
        }

        private void TombstoneMissingRows(IEnumerable<int> observedWidths, long sequence)
        {
            var observed = observedWidths.ToHashSet();
            foreach (int width in resolutions.Keys.Except(observed).ToArray())
            {
                OrderedResolution current = resolutions[width];
                if (current.Sequence < sequence)
                {
                    resolutions[width] = new OrderedResolution(null, null, sequence);
                }
            }
        }

        private void Prune(DateTimeOffset now)
        {
            if (activeReads.Count > 0)
            {
                return;
            }

            foreach (int width in resolutions.Keys.ToArray())
            {
                OrderedResolution current = resolutions[width];
                if (current.Resolution is null
                    || current.ReadStartedAt is null
                    || !IsCurrent(current.Resolution, current.ReadStartedAt.Value, now))
                {
                    resolutions.Remove(width);
                }
            }

            if (coverage is not null
                && now - coverage.ReadStartedAt >= negativeLifetime)
            {
                coverage = null;
            }
        }

        private sealed record Coverage(
            int[] GeneratedKeys,
            DateTimeOffset ReadStartedAt,
            long Sequence);

        private sealed record OrderedResolution(
            TrickplayMetadataResolution? Resolution,
            DateTimeOffset? ReadStartedAt,
            long Sequence);
    }
}
