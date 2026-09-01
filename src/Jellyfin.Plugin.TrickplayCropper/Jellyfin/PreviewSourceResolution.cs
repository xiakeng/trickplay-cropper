namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed outcomes of Jellyfin source resolution.
/// </summary>
internal abstract record PreviewSourceResolution
{
    /// <summary>
    /// Represents a fully authorized and resolved Source Sprite.
    /// </summary>
    /// <param name="Source">The resolved source snapshot.</param>
    internal sealed record Found(ResolvedPreviewSource Source) : PreviewSourceResolution;

    /// <summary>
    /// Represents a request without a usable current user.
    /// </summary>
    internal sealed record Unauthorized : PreviewSourceResolution;

    /// <summary>
    /// Represents an explicit playback-policy denial.
    /// </summary>
    internal sealed record Forbidden : PreviewSourceResolution;

    /// <summary>
    /// Represents an unavailable or concealed resource.
    /// </summary>
    internal sealed record NotFound : PreviewSourceResolution;
}
