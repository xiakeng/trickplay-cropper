using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed outcomes of the authorized Frame Timeline context.
/// </summary>
internal abstract record FrameTimelineContextResolution
{
    internal sealed record Resolved(long IntervalTicks, int FrameCount) : FrameTimelineContextResolution;

    internal sealed record Unauthorized : FrameTimelineContextResolution;

    internal sealed record Forbidden : FrameTimelineContextResolution;

    internal sealed record NotFound(PreviewUnavailableReason Reason) : FrameTimelineContextResolution;
}
