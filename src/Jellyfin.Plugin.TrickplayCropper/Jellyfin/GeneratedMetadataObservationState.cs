using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Owns ordered Generated Metadata Observations and active reads for one Source Video.
/// </summary>
internal sealed class GeneratedMetadataObservationState
{
    /// <summary>Gets the lifetime of reusable negative observations.</summary>
    public static readonly TimeSpan NegativeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Gets the lifetime of reusable positive observations.</summary>
    public static readonly TimeSpan PositiveLifetime = TimeSpan.FromMinutes(30);

    private readonly HashSet<ReadRegistration> activeReads = [];
    private readonly object gate = new();
    private readonly Dictionary<int, OrderedResolution> resolutions = [];
    private Coverage? coverage;
    private bool retired;

    /// <summary>Resolves a reusable observation or reserves a new host read.</summary>
    /// <param name="selectedResolution">The selected target width.</param>
    /// <param name="access">The caller's metadata access kind.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The lookup decision.</returns>
    public Lookup ResolveOrReserve(int selectedResolution, MetadataAccess access, DateTimeOffset now)
    {
        lock (gate)
        {
            if (retired)
            {
                return new Lookup.Retired();
            }

            Prune(now);
            OrderedResolution? current = FindCurrent(selectedResolution);
            if (CanReuse(current, access, now))
            {
                return new Lookup.Cached(current!.Resolution!);
            }

            var registration = new ReadRegistration();
            activeReads.Add(registration);
            return new Lookup.Reserved(registration);
        }
    }

    /// <summary>Publishes one successful host observation in read-start order.</summary>
    /// <param name="observation">The complete observed metadata rows and selected result.</param>
    /// <param name="selectedResolution">The selected target width.</param>
    /// <param name="stamp">The ordered read stamp.</param>
    public void Publish(
        Observation observation,
        int selectedResolution,
        Stamp stamp)
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
            if (!resolutions.TryGetValue(selectedResolution, out OrderedResolution? current)
                || current.Stamp.Sequence < stamp.Sequence)
            {
                resolutions[selectedResolution] = new OrderedResolution(observation.Resolution, stamp);
            }
        }
    }

    /// <summary>Rejects an unusable selected row without publishing it as reusable evidence.</summary>
    /// <param name="selectedResolution">The selected target width.</param>
    /// <param name="stamp">The ordered read stamp.</param>
    public void Reject(int selectedResolution, Stamp stamp)
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

    /// <summary>Completes an active read and reports whether this source state retired.</summary>
    /// <param name="registration">The active read registration.</param>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when the state retired.</returns>
    public bool Complete(ReadRegistration registration, DateTimeOffset now)
    {
        lock (gate)
        {
            activeReads.Remove(registration);
            Prune(now);
            return RetireIfEmpty();
        }
    }

    /// <summary>Prunes expired observations and retires empty source state.</summary>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when the state retired.</returns>
    public bool TryRetire(DateTimeOffset now)
    {
        lock (gate)
        {
            Prune(now);
            return RetireIfEmpty();
        }
    }

    /// <summary>Determines whether a resolution is current for its observation kind.</summary>
    /// <param name="resolution">The observed resolution.</param>
    /// <param name="readStartedAt">The host read start time.</param>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when the observation remains reusable.</returns>
    public static bool IsCurrent(
        TrickplayMetadataResolution resolution,
        DateTimeOffset readStartedAt,
        DateTimeOffset now)
    {
        TimeSpan age = now - readStartedAt;
        return age >= TimeSpan.Zero && age < GetLifetime(resolution);
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
        Stamp stamp)
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

    private void TombstoneMissingRows(IEnumerable<int> observedWidths, Stamp stamp)
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
            && now - coverage.Stamp.ReadStartedAt >= NegativeLifetime)
        {
            coverage = null;
        }
    }

    private static TimeSpan GetLifetime(TrickplayMetadataResolution resolution)
    {
        return resolution is TrickplayMetadataResolution.Available
            ? PositiveLifetime
            : NegativeLifetime;
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

    /// <summary>Identifies the caller's reuse contract.</summary>
    public enum MetadataAccess
    {
        /// <summary>Allows reuse of positive and negative observations.</summary>
        Probe,

        /// <summary>Allows reuse of negative observations only.</summary>
        Preview,
    }

    /// <summary>Identifies one active host read.</summary>
    public sealed class ReadRegistration
    {
    }

    /// <summary>Captures a complete host observation and the selected resolution result.</summary>
    /// <param name="Resolutions">All observed metadata rows keyed by width.</param>
    /// <param name="Resolution">The selected resolution result.</param>
    public sealed record Observation(
        IReadOnlyDictionary<int, TrickplayMetadata> Resolutions,
        TrickplayMetadataResolution Resolution);

    /// <summary>Orders a host read by its start time and monotonic sequence.</summary>
    /// <param name="ReadStartedAt">The host read start time.</param>
    /// <param name="Sequence">The monotonic read sequence.</param>
    public sealed record Stamp(DateTimeOffset ReadStartedAt, long Sequence);

    /// <summary>Represents the resolution or reservation decision for one lookup.</summary>
    public abstract record Lookup
    {
        /// <summary>Returns a reusable cached resolution.</summary>
        /// <param name="Resolution">The reusable resolution.</param>
        internal sealed record Cached(TrickplayMetadataResolution Resolution) : Lookup;

        /// <summary>Returns ownership of a new host read.</summary>
        /// <param name="Registration">The active read registration.</param>
        internal sealed record Reserved(ReadRegistration Registration) : Lookup;

        /// <summary>Reports that this source state retired during the lookup.</summary>
        internal sealed record Retired : Lookup;
    }

    private sealed record Coverage(int[] GeneratedKeys, Stamp Stamp);

    private sealed record OrderedResolution(
        TrickplayMetadataResolution? Resolution,
        Stamp Stamp);
}
