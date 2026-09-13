using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Jellyfin.Plugin.TrickplayCropper.Api;

/// <summary>
/// Binds the query-string portion of a Trickplay Preview request.
/// </summary>
public sealed class PreviewQueryParameters
{
    /// <summary>
    /// Gets the optional alternate media source identifier.
    /// </summary>
    public Guid? MediaSourceId { get; init; }

    /// <summary>
    /// Gets the requested zero-based generated frame index.
    /// </summary>
    [BindRequired]
    public int FrameIndex { get; init; }
}
