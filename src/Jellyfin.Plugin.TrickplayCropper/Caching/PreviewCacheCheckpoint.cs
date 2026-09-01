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

    /// <summary>
    /// Occurs after a cleanup run owns the single-run mutex and captures its fixed boundary.
    /// </summary>
    CleanupStarted,

    /// <summary>
    /// Occurs after cleanup captures a candidate fingerprint and before it requests entry ownership.
    /// </summary>
    CleanupCandidateCaptured,

    /// <summary>
    /// Occurs after cleanup acquires ownership of the candidate's Preview Cache Entry.
    /// </summary>
    CleanupEntryLeaseAcquired,
}
