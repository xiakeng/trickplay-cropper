using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed outcomes of the user-authorized GET Preview context.
/// </summary>
internal abstract record PreviewContextResolution
{
    /// <summary>
    /// Represents an authorized request whose Frame Index is selected.
    /// </summary>
    /// <param name="Context">The successful GET Preview context.</param>
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
    /// <param name="Reason">The stable internal reason, disclosed only through Debug diagnostics.</param>
    internal sealed record NotFound(PreviewUnavailableReason Reason) : PreviewContextResolution;
}
