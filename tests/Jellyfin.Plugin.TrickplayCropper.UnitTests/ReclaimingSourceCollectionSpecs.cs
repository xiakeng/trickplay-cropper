using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Xunit;

namespace Jellyfin.Plugin.TrickplayCropper.UnitTests;

public sealed class ReclaimingSourceCollectionSpecs
{
    [Fact]
    public void AnotherSourceLookupReclaimsExpiredIdleState()
    {
        var time = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero));
        var sources = new ReclaimingSourceCollection<ExpiringState>(
            time,
            TimeSpan.FromMinutes(5),
            static (state, now) => state.ExpiresAt <= now);
        Guid idleSourceId = Guid.NewGuid();
        Guid activeSourceId = Guid.NewGuid();
        var idleState = new ExpiringState(time.GetUtcNow().AddMinutes(30));

        Assert.Same(idleState, sources.GetOrAdd(idleSourceId, _ => idleState));

        time.Advance(TimeSpan.FromMinutes(30));
        var activeState = new ExpiringState(time.GetUtcNow().AddMinutes(30));
        Assert.Same(activeState, sources.GetOrAdd(activeSourceId, _ => activeState));

        var replacement = new ExpiringState(time.GetUtcNow().AddMinutes(30));
        Assert.Same(replacement, sources.GetOrAdd(idleSourceId, _ => replacement));
    }

    private sealed record ExpiringState(DateTimeOffset ExpiresAt);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long utcTicks;

        public ManualTimeProvider(DateTimeOffset initialTime)
        {
            utcTicks = initialTime.UtcTicks;
        }

        public void Advance(TimeSpan duration)
        {
            Interlocked.Add(ref utcTicks, duration.Ticks);
        }

        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(Volatile.Read(ref utcTicks), TimeSpan.Zero);
        }
    }
}
