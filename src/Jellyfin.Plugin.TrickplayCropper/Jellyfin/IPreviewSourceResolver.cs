namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Resolves the GET-only Source Sprite snapshot for a shared Preview context.
/// </summary>
internal interface IPreviewSourceResolver
{
    /// <summary>
    /// Selects the crop geometry and snapshots the manager-owned Source Sprite.
    /// </summary>
    /// <param name="context">The successful shared Preview context.</param>
    /// <returns>The typed source-resolution result.</returns>
    Task<PreviewSourceResolution> ResolveAsync(PreviewContext context);
}
