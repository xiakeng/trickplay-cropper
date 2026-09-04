namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed outcomes of the shared Preview context.
/// </summary>
internal abstract record PreviewContextResolution
{
    /// <summary>
    /// Represents an authorized request whose Frame Index is selected.
    /// </summary>
    /// <param name="Context">The shared Preview context.</param>
    internal sealed record Resolved(PreviewContext Context) : PreviewContextResolution;

    /// <summary>
    /// Represents an invalid request.
    /// </summary>
    internal sealed record BadRequest : PreviewContextResolution;

    /// <summary>
    /// Represents a request without a usable current user.
    /// </summary>
    internal sealed record Unauthorized : PreviewContextResolution;

    /// <summary>
    /// Represents an explicit playback-policy denial.
    /// </summary>
    internal sealed record Forbidden : PreviewContextResolution;

    /// <summary>
    /// Represents an unavailable or concealed resource.
    /// </summary>
    internal sealed record NotFound : PreviewContextResolution;
}
