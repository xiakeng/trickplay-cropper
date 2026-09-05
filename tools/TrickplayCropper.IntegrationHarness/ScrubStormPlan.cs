namespace TrickplayCropper.IntegrationHarness;

/// <summary>Creates replayable six-lane trajectories from the approved fixed seed.</summary>
internal sealed class ScrubStormPlan
{
    /// <summary>The approved replay seed.</summary>
    public const int Seed = 0x5EEDC0DE;

    /// <summary>The number of logical clients sharing playback subjects.</summary>
    public const int Clients = 2;

    /// <summary>The concurrent lanes belonging to each client.</summary>
    public const int LanesPerClient = 3;

    /// <summary>The request positions per lane per Item.</summary>
    public const int PositionsPerItem = 12;

    /// <summary>Builds one shape whose positions are replayed in both rounds.</summary>
    public static PreviewRequest[][] Create(IReadOnlyList<PreviewRequest> subjects, int shape)
    {
        return Enumerable.Range(0, Clients * LanesPerClient)
            .Select(lane => subjects.SelectMany(subject => CreatePositions(subject, shape, lane)).ToArray()).ToArray();
    }

    private static IEnumerable<PreviewRequest> CreatePositions(PreviewRequest subject, int shape, int lane)
    {
        Random random = new(Seed + (shape * 100) + lane);
        int count = subject.Metadata.Count;
        int window = Math.Min(count, 5);
        int anchor = (count - window) / 2;
        int[] frames = Enumerable.Range(0, PositionsPerItem).Select(index => shape switch
        {
            0 => index < 5 ? (int)((long)(count - 1) * index / 4) : random.Next(count),
            1 => (int)((long)(count - 1) * (lane % 2 == 0 ? Math.Min(index, 10) : 10 - Math.Min(index, 10)) / 10),
            2 => anchor + (index % window),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        }).ToArray();
        frames[^1] = frames[0];
        return frames.Select(frame => subject with
        {
            Ticks = checked((long)frame * subject.Metadata.Interval * TimeSpan.TicksPerMillisecond),
        });
    }
}
