namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Represents a normalized Trickplay Preview request.
/// </summary>
/// <param name="ItemId">The logical video identifier.</param>
/// <param name="MediaSourceId">The optional alternate media source identifier.</param>
/// <param name="PositionTicks">The playback position in Jellyfin ticks.</param>
public sealed record PreviewQuery(Guid ItemId, Guid? MediaSourceId, long PositionTicks)
{
    /// <summary>
    /// Gets the selected media source identifier, defaulting to the logical video.
    /// </summary>
    public Guid ResolvedMediaSourceId => MediaSourceId ?? ItemId;
}
