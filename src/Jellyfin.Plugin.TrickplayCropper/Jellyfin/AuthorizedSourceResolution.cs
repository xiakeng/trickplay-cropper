using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.TrickplayCropper.Preview;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Defines the closed results of current-user source authorization.
/// </summary>
internal abstract record AuthorizedSourceResolution
{
    /// <summary>
    /// Represents an authorized source and its matched source width.
    /// </summary>
    /// <param name="SourceVideo">The user-visible Source Video.</param>
    /// <param name="NormalizationSourceWidth">The matched Media Source video width.</param>
    internal sealed record Resolved(Video SourceVideo, int? NormalizationSourceWidth)
        : AuthorizedSourceResolution;

    /// <summary>Represents malformed request data.</summary>
    internal sealed record BadRequest : AuthorizedSourceResolution;

    /// <summary>Represents a request without a usable current user.</summary>
    internal sealed record Unauthorized : AuthorizedSourceResolution;

    /// <summary>Represents an explicit playback-policy denial.</summary>
    internal sealed record Forbidden : AuthorizedSourceResolution;

    /// <summary>Represents an unavailable or concealed resource.</summary>
    internal sealed record NotFound : AuthorizedSourceResolution;
}
