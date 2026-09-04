namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed outcomes of GET-only Source Sprite resolution.
/// </summary>
internal abstract record PreviewSourceResolution
{
    /// <summary>
    /// Represents a resolved and snapshotted Source Sprite.
    /// </summary>
    /// <param name="Source">The resolved source snapshot.</param>
    internal sealed record Found(ResolvedPreviewSource Source) : PreviewSourceResolution;

    /// <summary>
    /// Represents an absent Source Sprite.
    /// </summary>
    internal sealed record NotFound : PreviewSourceResolution;
}
