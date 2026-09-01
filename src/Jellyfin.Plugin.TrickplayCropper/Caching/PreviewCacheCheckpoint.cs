namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Identifies deterministic boundaries in Preview Cache Entry coordination and publication.
/// </summary>
internal enum PreviewCacheCheckpoint
{
    /// <summary>
    /// Occurs after shared Cache Tree ownership is acquired and before entry ownership is requested.
    /// </summary>
    TreeLeaseAcquired,

    /// <summary>
    /// Occurs after keyed Preview Cache Entry ownership is acquired.
    /// </summary>
    EntryLeaseAcquired,

    /// <summary>
    /// Occurs after the final-path recheck and immediately before no-overwrite publication.
    /// </summary>
    BeforePublication,

    /// <summary>
    /// Occurs immediately after this process publishes the final Preview Cache Entry.
    /// </summary>
    AfterPublication,

    /// <summary>
    /// Occurs after immutable response buffering and before entry and Cache Tree ownership are released.
    /// </summary>
    ResponseBuffered,
}
