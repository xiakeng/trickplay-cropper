namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Represents a normalized Trickplay Preview request.
/// </summary>
/// <param name="ItemId">The logical video identifier.</param>
/// <param name="MediaSourceId">The optional alternate media source identifier.</param>
/// <param name="FrameIndex">The requested zero-based generated frame index.</param>
public sealed record PreviewQuery(Guid ItemId, Guid? MediaSourceId, int FrameIndex)
{
    /// <summary>
    /// Gets the selected media source identifier, defaulting to the logical video.
    /// </summary>
    public Guid ResolvedMediaSourceId => MediaSourceId ?? ItemId;
}
