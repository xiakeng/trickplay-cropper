namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Identifies a legacy temporary candidate that requires an exclusive cache-tree lease.
/// </summary>
/// <param name="Path">The canonical candidate path.</param>
/// <param name="Length">The captured file length.</param>
/// <param name="LastWriteTimeUtcTicks">The captured last-write timestamp.</param>
internal sealed record ExclusiveCleanupCandidate(
    string Path,
    long Length,
    long LastWriteTimeUtcTicks)
    : CleanupCandidate(Path, Length, LastWriteTimeUtcTicks);
