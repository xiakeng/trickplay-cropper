using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed results of Frame Timeline metadata selection.
/// </summary>
internal abstract record FrameTimelineCalculationResolution
{
    internal sealed record Available(long IntervalTicks, int FrameCount) : FrameTimelineCalculationResolution;

    internal sealed record NotFound(PreviewUnavailableReason Reason) : FrameTimelineCalculationResolution;
}
