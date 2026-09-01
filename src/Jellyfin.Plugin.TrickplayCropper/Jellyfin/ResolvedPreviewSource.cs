using Jellyfin.Plugin.TrickplayCropper.Preview;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Contains a policy-approved Source Sprite snapshot and selected crop.
/// </summary>
/// <param name="MediaSourceId">The effective media source identifier.</param>
/// <param name="SourceSpritePath">The manager-owned Source Sprite path.</param>
/// <param name="SourceLength">The captured Source Sprite length.</param>
/// <param name="SourceLastWriteUtcTicks">The captured Source Sprite UTC modification ticks.</param>
/// <param name="Metadata">The exact 320px metadata.</param>
/// <param name="Selection">The selected frame and crop.</param>
internal sealed record ResolvedPreviewSource(
    Guid MediaSourceId,
    string SourceSpritePath,
    long SourceLength,
    long SourceLastWriteUtcTicks,
    TrickplayMetadata Metadata,
    FrameSelection Selection);
