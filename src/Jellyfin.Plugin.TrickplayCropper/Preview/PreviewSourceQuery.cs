namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Identifies the logical video and selected Media Source for an authorized request.
/// </summary>
/// <param name="ItemId">The logical video identifier.</param>
/// <param name="MediaSourceId">The optional alternate media source identifier.</param>
public sealed record PreviewSourceQuery(Guid ItemId, Guid? MediaSourceId)
{
    /// <summary>
    /// Gets the selected media source identifier, defaulting to the logical video.
    /// </summary>
    public Guid ResolvedMediaSourceId => MediaSourceId ?? ItemId;
}
