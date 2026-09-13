using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

internal abstract record FrameTimelineContextResolution
{
    internal sealed record Resolved(TrickplayMetadata Metadata) : FrameTimelineContextResolution;

    internal sealed record BadRequest : FrameTimelineContextResolution;

    internal sealed record Unauthorized : FrameTimelineContextResolution;

    internal sealed record Forbidden : FrameTimelineContextResolution;

    internal sealed record NotFound : FrameTimelineContextResolution;
}
