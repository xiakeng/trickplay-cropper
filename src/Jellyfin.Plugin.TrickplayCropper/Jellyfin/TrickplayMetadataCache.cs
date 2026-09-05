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

    private readonly ReclaimingSourceCollection<SourceState> sources;
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
        sources = new ReclaimingSourceCollection<SourceState>(
            timeProvider,
            negativeLifetime,
            static (source, now) => source.TryRetire(now));
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
        DateTimeOffset now = timeProvider.GetUtcNow();
        MetadataLookup lookup = GetSourceForRequest(request, now);
        if (lookup is MetadataLookup.Cached cached)
        {
            return cached.Resolution;
        }

        if (lookup is not MetadataLookup.Missed missed)
        {
            throw new InvalidOperationException($"Unknown metadata lookup {lookup.GetType().Name}.");
        }

        IssuedRead issued = IssueRead(missed, request);
        Task<ReadOutcome> read = ReadAndPublishAsync(missed.Source, request, issued);
        ReadOutcome outcome = await read.WaitAsync(cancellationToken).ConfigureAwait(false);
        return outcome switch
        {
            ReadOutcome.Succeeded succeeded => succeeded.Resolution,
            ReadOutcome.Failed failed => ThrowFailure(failed),
            _ => throw new InvalidOperationException($"Unknown metadata read outcome {outcome.GetType().Name}."),
        };
    }

    private MetadataLookup GetSourceForRequest(MetadataRequest request, DateTimeOffset now)
    {
        while (true)
        {
            SourceState source = sources.GetOrAdd(request.SourceVideoId, static _ => new SourceState());
            SourceLookup lookup = source.ResolveOrReserve(request, now);
            if (lookup is SourceLookup.Cached cached)
            {
                return new MetadataLookup.Cached(cached.Resolution);
            }

            if (lookup is SourceLookup.Reserved reserved)
            {
                return new MetadataLookup.Missed(source, reserved.Registration);
            }

            sources.TryRemove(request.SourceVideoId, source);
        }
    }

    private IssuedRead IssueRead(MetadataLookup.Missed lookup, MetadataRequest request)
    {
        var stamp = new ObservationStamp(
            timeProvider.GetUtcNow(),
            Interlocked.Increment(ref nextObservationSequence));
        Task<Dictionary<int, TrickplayInfo>> query;
        try
        {
            query = trickplayManager.GetTrickplayResolutions(request.SourceVideoId);
        }
        catch (Exception failure)
        {
            query = Task.FromException<Dictionary<int, TrickplayInfo>>(failure);
        }

        return new IssuedRead(lookup.Registration, stamp, query);
    }

    private async Task<ReadOutcome> ReadAndPublishAsync(
        SourceState source,
        MetadataRequest request,
        IssuedRead issued)
    {
        try
        {
            Dictionary<int, TrickplayInfo> resolutions = await issued.Query.ConfigureAwait(false);
            MetadataObservation observation = CreateObservation(request.SelectedResolution, resolutions);
            DateTimeOffset completedAt = timeProvider.GetUtcNow();
            if (!IsCurrent(observation.Resolution, issued.Stamp.ReadStartedAt, completedAt))
            {
                source.Reject(request.SelectedResolution, issued.Stamp);
                return ReadOutcome.Capture(
                    new InvalidOperationException(
                        "The generated Trickplay metadata observation expired before its read completed."));
            }

            source.Publish(observation, request, issued.Stamp);
            return new ReadOutcome.Succeeded(observation.Resolution);
        }
        catch (InvalidTrickplayMetadataException failure)
        {
            source.Reject(request.SelectedResolution, issued.Stamp);
            return ReadOutcome.Capture(failure);
        }
        catch (Exception failure)
        {
            return ReadOutcome.Capture(failure);
        }
        finally
        {
            if (source.Complete(issued.Registration, timeProvider.GetUtcNow()))
            {
                sources.TryRemove(request.SourceVideoId, source);
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

    private sealed record ObservationStamp(DateTimeOffset ReadStartedAt, long Sequence);

    private sealed class ReadRegistration
    {
    }

    private sealed record IssuedRead(
        ReadRegistration Registration,
        ObservationStamp Stamp,
        Task<Dictionary<int, TrickplayInfo>> Query);

    private abstract record MetadataLookup
    {
        internal sealed record Cached(TrickplayMetadataResolution Resolution) : MetadataLookup;

        internal sealed record Missed(SourceState Source, ReadRegistration Registration) : MetadataLookup;
    }

    private abstract record SourceLookup
    {
        internal sealed record Cached(TrickplayMetadataResolution Resolution) : SourceLookup;

        internal sealed record Reserved(ReadRegistration Registration) : SourceLookup;

        internal sealed record Retired : SourceLookup;
    }

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
        private readonly HashSet<ReadRegistration> activeReads = [];
        private readonly object gate = new();
        private readonly Dictionary<int, OrderedResolution> resolutions = [];
        private Coverage? coverage;
        private bool retired;

        public SourceLookup ResolveOrReserve(MetadataRequest request, DateTimeOffset now)
        {
            lock (gate)
            {
                if (retired)
                {
                    return new SourceLookup.Retired();
                }

                Prune(now);
                OrderedResolution? current = FindCurrent(request.SelectedResolution);
                if (CanReuse(current, request.Access, now))
                {
                    return new SourceLookup.Cached(current!.Resolution!);
                }

                var registration = new ReadRegistration();
                activeReads.Add(registration);
                return new SourceLookup.Reserved(registration);
            }
        }

        public void Publish(
            MetadataObservation observation,
            MetadataRequest request,
            ObservationStamp stamp)
        {
            lock (gate)
            {
                if (coverage is not null && coverage.Stamp.Sequence > stamp.Sequence)
                {
                    return;
                }

                PublishObservedRows(observation.Resolutions, stamp);
                TombstoneMissingRows(observation.Resolutions.Keys, stamp);
                coverage = new Coverage(observation.Resolutions.Keys.ToArray(), stamp);
                if (!resolutions.TryGetValue(request.SelectedResolution, out OrderedResolution? current)
                    || current.Stamp.Sequence < stamp.Sequence)
                {
                    resolutions[request.SelectedResolution] = new OrderedResolution(observation.Resolution, stamp);
                }
            }
        }

        public void Reject(int selectedResolution, ObservationStamp stamp)
        {
            lock (gate)
            {
                if (!resolutions.TryGetValue(selectedResolution, out OrderedResolution? current)
                    || current.Stamp.Sequence < stamp.Sequence)
                {
                    resolutions[selectedResolution] = new OrderedResolution(null, stamp);
                }
            }
        }

        public bool Complete(ReadRegistration registration, DateTimeOffset now)
        {
            lock (gate)
            {
                activeReads.Remove(registration);
                Prune(now);
                return RetireIfEmpty();
            }
        }

        public bool TryRetire(DateTimeOffset now)
        {
            lock (gate)
            {
                Prune(now);
                return RetireIfEmpty();
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
                && (derived.Stamp.Sequence > selected.Stamp.Sequence
                    || (derived.Stamp.Sequence == selected.Stamp.Sequence && selected.Resolution is null));
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
                coverage.Stamp);
        }

        private static bool CanReuse(
            OrderedResolution? current,
            MetadataAccess access,
            DateTimeOffset now)
        {
            if (current?.Resolution is null)
            {
                return false;
            }

            if (access == MetadataAccess.Preview
                && current.Resolution is TrickplayMetadataResolution.Available)
            {
                return false;
            }

            return IsCurrent(current.Resolution, current.Stamp.ReadStartedAt, now);
        }

        private void PublishObservedRows(
            IReadOnlyDictionary<int, TrickplayMetadata> observed,
            ObservationStamp stamp)
        {
            foreach ((int width, TrickplayMetadata metadata) in observed)
            {
                resolutions.TryGetValue(width, out OrderedResolution? current);
                if (current is not null && current.Stamp.Sequence >= stamp.Sequence)
                {
                    continue;
                }

                TrickplayMetadataResolution? reusable = CreateReusableResolution(width, metadata);
                resolutions[width] = new OrderedResolution(reusable, stamp);
            }
        }

        private void TombstoneMissingRows(IEnumerable<int> observedWidths, ObservationStamp stamp)
        {
            var observed = observedWidths.ToHashSet();
            foreach (int width in resolutions.Keys.Except(observed).ToArray())
            {
                OrderedResolution current = resolutions[width];
                if (current.Stamp.Sequence < stamp.Sequence)
                {
                    resolutions[width] = new OrderedResolution(null, stamp);
                }
            }
        }

        private bool RetireIfEmpty()
        {
            if (activeReads.Count == 0 && coverage is null && resolutions.Count == 0)
            {
                retired = true;
            }

            return retired;
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
                    || !IsCurrent(current.Resolution, current.Stamp.ReadStartedAt, now))
                {
                    resolutions.Remove(width);
                }
            }

            if (coverage is not null
                && now - coverage.Stamp.ReadStartedAt >= negativeLifetime)
            {
                coverage = null;
            }
        }

        private sealed record Coverage(int[] GeneratedKeys, ObservationStamp Stamp);

        private sealed record OrderedResolution(
            TrickplayMetadataResolution? Resolution,
            ObservationStamp Stamp);
    }
}
