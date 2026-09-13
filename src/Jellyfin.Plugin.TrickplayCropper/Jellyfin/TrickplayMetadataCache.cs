using System.Runtime.ExceptionServices;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Trickplay;
using MetadataAccess = Jellyfin.Plugin.TrickplayCropper.Jellyfin.GeneratedMetadataObservationState.MetadataAccess;
using MetadataObservation = Jellyfin.Plugin.TrickplayCropper.Jellyfin.GeneratedMetadataObservationState.Observation;
using ObservationStamp = Jellyfin.Plugin.TrickplayCropper.Jellyfin.GeneratedMetadataObservationState.Stamp;
using ReadRegistration = Jellyfin.Plugin.TrickplayCropper.Jellyfin.GeneratedMetadataObservationState.ReadRegistration;
using SourceLookup = Jellyfin.Plugin.TrickplayCropper.Jellyfin.GeneratedMetadataObservationState.Lookup;
using SourceState = Jellyfin.Plugin.TrickplayCropper.Jellyfin.GeneratedMetadataObservationState;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Owns immutable generated-metadata observations shared by Trickplay Frame Probes and Preview requests.
/// </summary>
internal sealed class TrickplayMetadataCache
{
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
            GeneratedMetadataObservationState.NegativeLifetime,
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

    /// <summary>
    /// Reads one authoritative metadata snapshot without publishing or consulting observations.
    /// </summary>
    public async Task<TrickplayMetadataResolution> ReadAuthoritativeAsync(
        Guid sourceVideoId,
        int selectedResolution,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<int, TrickplayInfo> resolutions = await trickplayManager
            .GetTrickplayResolutions(sourceVideoId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return Resolve(selectedResolution, CopyMetadata(resolutions));
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
            SourceLookup lookup = source.ResolveOrReserve(request.SelectedResolution, request.Access, now);
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
            if (!GeneratedMetadataObservationState.IsCurrent(
                    observation.Resolution,
                    issued.Stamp.ReadStartedAt,
                    completedAt))
            {
                source.Reject(request.SelectedResolution, issued.Stamp);
                return ReadOutcome.Capture(
                    new InvalidOperationException(
                        "The generated Trickplay metadata observation expired before its read completed."));
            }

            source.Publish(observation, request.SelectedResolution, issued.Stamp);
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

    private static TrickplayMetadataResolution ThrowFailure(ReadOutcome.Failed failure)
    {
        failure.Failure.Throw();
        throw new InvalidOperationException("The captured metadata failure did not throw.");
    }

    private sealed record MetadataRequest(
        Guid SourceVideoId,
        int SelectedResolution,
        MetadataAccess Access);

    private sealed record IssuedRead(
        ReadRegistration Registration,
        ObservationStamp Stamp,
        Task<Dictionary<int, TrickplayInfo>> Query);

    private abstract record MetadataLookup
    {
        internal sealed record Cached(TrickplayMetadataResolution Resolution) : MetadataLookup;

        internal sealed record Missed(SourceState Source, ReadRegistration Registration) : MetadataLookup;
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

}
