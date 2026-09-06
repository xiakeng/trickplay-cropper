using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Owns immutable user-independent Item membership and matched-source width facts.
/// </summary>
internal sealed class TrickplaySourceFactsCache
{
    private static readonly TimeSpan negativeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan positiveLifetime = TimeSpan.FromMinutes(30);

    private readonly ReclaimingSourceCollection<ItemState> items;
    private readonly ILibraryManager libraryManager;
    private readonly IMediaSourceManager mediaSourceManager;
    private readonly TimeProvider timeProvider;
    private long nextObservationSequence;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplaySourceFactsCache"/> class.
    /// </summary>
    /// <param name="libraryManager">Resolves Item and Source Video identity without user visibility filtering.</param>
    /// <param name="mediaSourceManager">Enumerates all supported Media Sources without user shaping.</param>
    /// <param name="timeProvider">Provides authoritative source read-start time.</param>
    public TrickplaySourceFactsCache(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        TimeProvider timeProvider)
    {
        this.libraryManager = libraryManager;
        this.mediaSourceManager = mediaSourceManager;
        this.timeProvider = timeProvider;
        items = new ReclaimingSourceCollection<ItemState>(
            timeProvider,
            negativeLifetime,
            static (item, now) => item.TryRetire(now));
    }

    /// <summary>
    /// Reuses current source facts for a Trickplay Frame Probe or reads an authoritative replacement.
    /// </summary>
    /// <param name="query">The normalized Preview query.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The verified immutable source facts or explicit absence.</returns>
    public async Task<TrickplaySourceFactsResolution> GetForProbeAsync(
        PreviewQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SourceLookup lookup = GetItemForProbe(query, timeProvider.GetUtcNow());
        if (lookup is SourceLookup.Cached cached)
        {
            return cached.Resolution;
        }

        if (lookup is not SourceLookup.Reserved reserved)
        {
            throw new InvalidOperationException($"Unknown source-facts lookup {lookup.GetType().Name}.");
        }

        ObservationStamp stamp = CreateStamp();
        try
        {
            TrickplaySourceFactsResolution loaded = await LoadAsync(query, cancellationToken).ConfigureAwait(false);
            PublishCurrent(reserved.Item, query.ResolvedMediaSourceId, loaded, stamp);
            return loaded;
        }
        finally
        {
            Complete(query.ItemId, query.ResolvedMediaSourceId, reserved.Item, reserved.Registration);
        }
    }

    /// <summary>
    /// Starts the authoritative source read performed by a current user-authorized Preview request.
    /// </summary>
    /// <param name="query">The normalized Preview query.</param>
    /// <returns>The ordered read-start observation used if the request verifies source facts.</returns>
    public PreviewObservation BeginForPreview(PreviewQuery query)
    {
        while (true)
        {
            ItemState item = items.GetOrAdd(query.ItemId, static _ => new ItemState());
            ReadRegistration? registration = item.Reserve(query.ResolvedMediaSourceId);
            if (registration is not null)
            {
                return new PreviewObservation(
                    this,
                    query.ItemId,
                    query.ResolvedMediaSourceId,
                    item,
                    registration,
                    CreateStamp());
            }

            items.TryRemove(query.ItemId, item);
        }
    }

    /// <summary>
    /// Publishes source facts independently verified by a current user-authorized Preview request.
    /// </summary>
    /// <param name="observation">The Preview request's authoritative source read.</param>
    /// <param name="normalizationSourceWidth">The matched Media Source video-stream width.</param>
    public void PublishForPreview(PreviewObservation observation, int? normalizationSourceWidth)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var available = new TrickplaySourceFactsResolution.Available(normalizationSourceWidth);
        PublishCurrent(observation.Item, observation.SourceVideoId, available, observation.Stamp);
    }

    private SourceLookup GetItemForProbe(PreviewQuery query, DateTimeOffset now)
    {
        while (true)
        {
            ItemState item = items.GetOrAdd(query.ItemId, static _ => new ItemState());
            SourceLookup lookup = item.ResolveOrReserve(query.ResolvedMediaSourceId, now);
            if (lookup is not SourceLookup.Retired)
            {
                return lookup;
            }

            items.TryRemove(query.ItemId, item);
        }
    }

    private ObservationStamp CreateStamp()
    {
        return new ObservationStamp(
            timeProvider.GetUtcNow(),
            Interlocked.Increment(ref nextObservationSequence));
    }

    private void PublishCurrent(
        ItemState item,
        Guid sourceVideoId,
        TrickplaySourceFactsResolution resolution,
        ObservationStamp stamp)
    {
        if (!IsCurrent(resolution, stamp.ReadStartedAt, timeProvider.GetUtcNow()))
        {
            item.Reject(sourceVideoId, stamp);
            throw new InvalidOperationException(
                "The Trickplay source-facts observation expired before its read completed.");
        }

        item.Publish(sourceVideoId, resolution, stamp);
    }

    private void Complete(
        Guid itemId,
        Guid sourceVideoId,
        ItemState item,
        ReadRegistration registration)
    {
        if (item.Complete(sourceVideoId, registration, timeProvider.GetUtcNow()))
        {
            items.TryRemove(itemId, item);
        }
    }

    private async Task<TrickplaySourceFactsResolution> LoadAsync(
        PreviewQuery query,
        CancellationToken cancellationToken)
    {
        Video? logicalVideo = libraryManager.GetItemById<Video>(query.ItemId);
        if (logicalVideo?.Id != query.ItemId)
        {
            return new TrickplaySourceFactsResolution.NotFound();
        }

        IReadOnlyList<MediaSourceInfo> mediaSources = await mediaSourceManager.GetPlaybackMediaSources(
            logicalVideo,
            user: null!,
            allowMediaProbe: false,
            enablePathSubstitution: false,
            cancellationToken).ConfigureAwait(false);
        MediaSourceInfo? matchedSource = mediaSources.FirstOrDefault(
            source => IsSelectedSource(source, query.ResolvedMediaSourceId));
        if (matchedSource is null)
        {
            return new TrickplaySourceFactsResolution.NotFound();
        }

        Video? sourceVideo = libraryManager.GetItemById<Video>(query.ResolvedMediaSourceId);
        return sourceVideo?.Id == query.ResolvedMediaSourceId
            ? new TrickplaySourceFactsResolution.Available(matchedSource.VideoStream?.Width)
            : new TrickplaySourceFactsResolution.NotFound();
    }

    private static bool IsSelectedSource(MediaSourceInfo source, Guid mediaSourceId)
    {
        return Guid.TryParse(source.Id, out Guid candidateId) && candidateId == mediaSourceId;
    }

    private static bool IsCurrent(
        TrickplaySourceFactsResolution resolution,
        DateTimeOffset readStartedAt,
        DateTimeOffset now)
    {
        TimeSpan age = now - readStartedAt;
        TimeSpan lifetime = resolution is TrickplaySourceFactsResolution.Available
            ? positiveLifetime
            : negativeLifetime;
        return age >= TimeSpan.Zero && age < lifetime;
    }

    /// <summary>
    /// Retains one Preview request's ordered source read until it settles.
    /// </summary>
    internal sealed class PreviewObservation : IDisposable
    {
        private TrickplaySourceFactsCache? owner;

        /// <summary>
        /// Initializes one registered Preview source observation.
        /// </summary>
        internal PreviewObservation(
            TrickplaySourceFactsCache owner,
            Guid itemId,
            Guid sourceVideoId,
            ItemState item,
            ReadRegistration registration,
            ObservationStamp stamp)
        {
            this.owner = owner;
            ItemId = itemId;
            SourceVideoId = sourceVideoId;
            Item = item;
            Registration = registration;
            Stamp = stamp;
        }

        /// <summary>
        /// Gets the logical Item identifier.
        /// </summary>
        internal Guid ItemId { get; }

        /// <summary>
        /// Gets the retained per-Item state.
        /// </summary>
        internal ItemState Item { get; }

        /// <summary>
        /// Gets the active read registration.
        /// </summary>
        internal ReadRegistration Registration { get; }

        /// <summary>
        /// Gets the effective Source Video identifier.
        /// </summary>
        internal Guid SourceVideoId { get; }

        /// <summary>
        /// Gets the authoritative read-start stamp.
        /// </summary>
        internal ObservationStamp Stamp { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            TrickplaySourceFactsCache? currentOwner = Interlocked.Exchange(ref owner, null);
            currentOwner?.Complete(ItemId, SourceVideoId, Item, Registration);
        }
    }

    /// <summary>
    /// Orders one source read and retains its authoritative start time.
    /// </summary>
    internal sealed record ObservationStamp(DateTimeOffset ReadStartedAt, long Sequence);

    /// <summary>
    /// Identifies one active authoritative source read.
    /// </summary>
    internal sealed class ReadRegistration;

    private sealed record SourceFactsObservation(
        TrickplaySourceFactsResolution Resolution,
        ObservationStamp Stamp);

    /// <summary>
    /// Defines the closed outcomes of consulting one retained source-fact state.
    /// </summary>
    internal abstract record SourceLookup
    {
        internal sealed record Cached(TrickplaySourceFactsResolution Resolution) : SourceLookup;

        internal sealed record Reserved(ItemState Item, ReadRegistration Registration) : SourceLookup;

        internal sealed record Retired : SourceLookup;
    }

    /// <summary>
    /// Retains independently aged source facts beneath one logical Item.
    /// </summary>
    internal sealed class ItemState
    {
        private readonly object gate = new();
        private readonly Dictionary<Guid, SourceState> sources = [];
        private bool retired;

        /// <summary>
        /// Reuses current facts or reserves an independent probe read.
        /// </summary>
        public SourceLookup ResolveOrReserve(Guid sourceVideoId, DateTimeOffset now)
        {
            lock (gate)
            {
                if (retired)
                {
                    return new SourceLookup.Retired();
                }

                SourceState source = GetOrCreateSource(sourceVideoId);
                Prune(sourceVideoId, source, now);
                source = GetOrCreateSource(sourceVideoId);
                if (source.Observation is not null
                    && IsCurrent(
                        source.Observation.Resolution,
                        source.Observation.Stamp.ReadStartedAt,
                        now))
                {
                    return new SourceLookup.Cached(source.Observation.Resolution);
                }

                var registration = new ReadRegistration();
                source.ActiveReads.Add(registration);
                return new SourceLookup.Reserved(this, registration);
            }
        }

        /// <summary>
        /// Reserves an independent user-authorized Preview read without reusing cached authority.
        /// </summary>
        public ReadRegistration? Reserve(Guid sourceVideoId)
        {
            lock (gate)
            {
                if (retired)
                {
                    return null;
                }

                SourceState source = GetOrCreateSource(sourceVideoId);
                var registration = new ReadRegistration();
                source.ActiveReads.Add(registration);
                return registration;
            }
        }

        /// <summary>
        /// Publishes a source observation when no newer read has already published.
        /// </summary>
        public void Publish(
            Guid sourceVideoId,
            TrickplaySourceFactsResolution resolution,
            ObservationStamp stamp)
        {
            lock (gate)
            {
                SourceState source = GetOrCreateSource(sourceVideoId);
                if (source.LatestSequence < stamp.Sequence)
                {
                    source.LatestSequence = stamp.Sequence;
                    source.Observation = new SourceFactsObservation(resolution, stamp);
                }
            }
        }

        /// <summary>
        /// Preserves newer expired evidence until older independent reads settle.
        /// </summary>
        public void Reject(Guid sourceVideoId, ObservationStamp stamp)
        {
            lock (gate)
            {
                SourceState source = GetOrCreateSource(sourceVideoId);
                if (source.LatestSequence < stamp.Sequence)
                {
                    source.LatestSequence = stamp.Sequence;
                    source.Observation = null;
                }
            }
        }

        /// <summary>
        /// Completes one read and reports whether the whole Item state can be removed.
        /// </summary>
        public bool Complete(
            Guid sourceVideoId,
            ReadRegistration registration,
            DateTimeOffset now)
        {
            lock (gate)
            {
                if (sources.TryGetValue(sourceVideoId, out SourceState? source))
                {
                    source.ActiveReads.Remove(registration);
                    Prune(sourceVideoId, source, now);
                }

                return RetireIfEmpty();
            }
        }

        /// <summary>
        /// Reclaims expired idle source facts and reports whether the Item can be removed.
        /// </summary>
        public bool TryRetire(DateTimeOffset now)
        {
            lock (gate)
            {
                foreach ((Guid sourceVideoId, SourceState source) in sources.ToArray())
                {
                    Prune(sourceVideoId, source, now);
                }

                return RetireIfEmpty();
            }
        }

        private SourceState GetOrCreateSource(Guid sourceVideoId)
        {
            if (!sources.TryGetValue(sourceVideoId, out SourceState? source))
            {
                source = new SourceState();
                sources.Add(sourceVideoId, source);
            }

            return source;
        }

        private static bool IsExpired(SourceFactsObservation observation, DateTimeOffset now)
        {
            return !IsCurrent(observation.Resolution, observation.Stamp.ReadStartedAt, now);
        }

        private void Prune(Guid sourceVideoId, SourceState source, DateTimeOffset now)
        {
            if (source.ActiveReads.Count > 0)
            {
                return;
            }

            if (source.Observation is not null && IsExpired(source.Observation, now))
            {
                source.Observation = null;
            }

            if (source.Observation is null)
            {
                sources.Remove(sourceVideoId);
            }
        }

        private bool RetireIfEmpty()
        {
            if (sources.Count == 0)
            {
                retired = true;
            }

            return retired;
        }

        private sealed class SourceState
        {
            public long LatestSequence { get; set; }

            public HashSet<ReadRegistration> ActiveReads { get; } = [];

            public SourceFactsObservation? Observation { get; set; }
        }
    }
}
