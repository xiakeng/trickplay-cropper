namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Names why one Preview request resolved to an unavailable outcome.
/// </summary>
/// <remarks>
/// Every value maps to the same concealed client outcome. Only <see cref="Concealed"/> suppresses
/// Debug logging; the remaining values are the stable Debug reasons of the Preview decision protocol
/// and never reach the client.
/// </remarks>
internal enum PreviewUnavailableReason
{
    /// <summary>
    /// The item or Media Source is hidden, absent, or not a member, so no reason is disclosed.
    /// </summary>
    Concealed,

    /// <summary>
    /// Jellyfin exposes no Trickplay Resolution Target.
    /// </summary>
    NoConfiguredTarget,

    /// <summary>
    /// The effective Source Video has no generated Trickplay metadata.
    /// </summary>
    NoGeneratedMetadata,

    /// <summary>
    /// Generated metadata exists but holds no exact Selected Trickplay Resolution entry.
    /// </summary>
    SelectedResolutionMissing,

    /// <summary>
    /// The selected metadata reports no available frames.
    /// </summary>
    NoThumbnails,

    /// <summary>
    /// The selected Source Sprite is unavailable at the GET availability boundary.
    /// </summary>
    SourceSpriteUnavailable,
}
