namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Defines every outcome produced by the Frame Timeline request module.
/// </summary>
public abstract record TrickplayFrameTimelineOutcome
{
    /// <summary>
    /// Represents a successful response with timeline calculation inputs.
    /// </summary>
    /// <param name="IntervalTicks">The positive frame interval in Jellyfin ticks.</param>
    /// <param name="FrameCount">The positive generated frame count.</param>
    public sealed record Ok(long IntervalTicks, int FrameCount) : TrickplayFrameTimelineOutcome;

    /// <summary>
    /// Represents a request without a usable authenticated user.
    /// </summary>
    public sealed record Unauthorized : TrickplayFrameTimelineOutcome;

    /// <summary>
    /// Represents an authenticated request without playback permission.
    /// </summary>
    public sealed record Forbidden : TrickplayFrameTimelineOutcome;

    /// <summary>
    /// Represents an unavailable or concealed resource.
    /// </summary>
    public sealed record NotFound : TrickplayFrameTimelineOutcome;

    /// <summary>
    /// Represents an unexpected request-processing failure.
    /// </summary>
    public sealed record InternalError : TrickplayFrameTimelineOutcome;
}
