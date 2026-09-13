namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Defines every outcome produced by the Frame Timeline request module.
/// </summary>
public abstract record TrickplayFrameTimelineOutcome
{
    public sealed record Ok(long IntervalTicks, int FrameCount) : TrickplayFrameTimelineOutcome;

    public sealed record Unauthorized : TrickplayFrameTimelineOutcome;

    public sealed record Forbidden : TrickplayFrameTimelineOutcome;

    public sealed record NotFound : TrickplayFrameTimelineOutcome;

    public sealed record InternalError : TrickplayFrameTimelineOutcome;
}
