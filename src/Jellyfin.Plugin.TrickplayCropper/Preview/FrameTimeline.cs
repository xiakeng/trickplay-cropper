using System.Security.Claims;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Coordinates one authenticated Frame Timeline request.
/// </summary>
public interface IFrameTimeline
{
    /// <summary>Resolves the current interval and generated frame count.</summary>
    Task<FrameTimelineOutcome> GetAsync(
        PreviewSourceQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}

/// <summary>Defines the closed outcomes of a Frame Timeline request.</summary>
public abstract record FrameTimelineOutcome
{
    /// <summary>Represents a successful timeline.</summary>
    /// <param name="IntervalTicks">The positive interval in Jellyfin ticks.</param>
    /// <param name="FrameCount">The positive generated frame count.</param>
    public sealed record Success(long IntervalTicks, int FrameCount) : FrameTimelineOutcome;

    /// <summary>Represents malformed request data.</summary>
    public sealed record BadRequest : FrameTimelineOutcome;

    /// <summary>Represents a request without a usable current user.</summary>
    public sealed record Unauthorized : FrameTimelineOutcome;

    /// <summary>Represents an explicit playback-policy denial.</summary>
    public sealed record Forbidden : FrameTimelineOutcome;

    /// <summary>Represents unavailable or concealed content.</summary>
    public sealed record NotFound : FrameTimelineOutcome;

    /// <summary>Represents invalid or failed server data.</summary>
    public sealed record InternalError : FrameTimelineOutcome;
}
