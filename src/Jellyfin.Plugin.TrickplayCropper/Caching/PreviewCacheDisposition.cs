namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Describes whether a Preview Cache Entry was read or generated.
/// </summary>
public enum PreviewCacheDisposition
{
    /// <summary>
    /// The existing Preview Cache Entry was read.
    /// </summary>
    Hit,

    /// <summary>
    /// A new Preview Cache Entry was generated.
    /// </summary>
    Miss,
}
