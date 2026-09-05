using System.Collections.Concurrent;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Retains source-scoped state and opportunistically reclaims entries that have expired.
/// </summary>
/// <typeparam name="TState">The retained source-state type.</typeparam>
internal sealed class ReclaimingSourceCollection<TState>
    where TState : class
{
    private readonly TimeSpan reclamationInterval;
    private readonly Func<TState, DateTimeOffset, bool> shouldReclaim;
    private readonly ConcurrentDictionary<Guid, TState> sources = new();
    private readonly TimeProvider timeProvider;
    private long nextReclamationUtcTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReclaimingSourceCollection{TState}"/> class.
    /// </summary>
    /// <param name="timeProvider">Provides the time used to schedule reclamation passes.</param>
    /// <param name="reclamationInterval">The minimum interval between reclamation passes.</param>
    /// <param name="shouldReclaim">Retires expired state and reports whether it can be removed.</param>
    public ReclaimingSourceCollection(
        TimeProvider timeProvider,
        TimeSpan reclamationInterval,
        Func<TState, DateTimeOffset, bool> shouldReclaim)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(shouldReclaim);
        if (reclamationInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reclamationInterval),
                reclamationInterval,
                "The reclamation interval must be positive.");
        }

        this.timeProvider = timeProvider;
        this.reclamationInterval = reclamationInterval;
        this.shouldReclaim = shouldReclaim;
        nextReclamationUtcTicks = timeProvider.GetUtcNow().Add(reclamationInterval).UtcTicks;
    }

    /// <summary>
    /// Reclaims expired entries when due and returns the retained or newly created state for a source.
    /// </summary>
    /// <param name="sourceVideoId">The effective Source Video identifier.</param>
    /// <param name="stateFactory">Creates state when the source is not retained.</param>
    /// <returns>The state retained for the source.</returns>
    public TState GetOrAdd(Guid sourceVideoId, Func<Guid, TState> stateFactory)
    {
        ArgumentNullException.ThrowIfNull(stateFactory);
        ReclaimExpiredSources(timeProvider.GetUtcNow());
        return sources.GetOrAdd(sourceVideoId, stateFactory);
    }

    /// <summary>
    /// Removes the source only when it still maps to the expected state instance.
    /// </summary>
    /// <param name="sourceVideoId">The effective Source Video identifier.</param>
    /// <param name="state">The expected retained state.</param>
    /// <returns><see langword="true"/> when the matching entry was removed.</returns>
    public bool TryRemove(Guid sourceVideoId, TState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return sources.TryRemove(new KeyValuePair<Guid, TState>(sourceVideoId, state));
    }

    private void ReclaimExpiredSources(DateTimeOffset now)
    {
        long scheduledAt = Volatile.Read(ref nextReclamationUtcTicks);
        if (now.UtcTicks < scheduledAt)
        {
            return;
        }

        long nextScheduledAt = now.Add(reclamationInterval).UtcTicks;
        if (Interlocked.CompareExchange(ref nextReclamationUtcTicks, nextScheduledAt, scheduledAt) != scheduledAt)
        {
            return;
        }

        foreach ((Guid sourceVideoId, TState state) in sources)
        {
            if (shouldReclaim(state, now))
            {
                TryRemove(sourceVideoId, state);
            }
        }
    }
}
