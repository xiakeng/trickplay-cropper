namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed class ManualTimeProvider : TimeProvider
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
