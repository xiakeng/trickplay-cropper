namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Identifies a cache entry candidate that coordinates deletion by its final entry path.
/// </summary>
/// <param name="Path">The canonical candidate path.</param>
/// <param name="LockPath">The final entry path used for coordination.</param>
/// <param name="Length">The captured file length.</param>
/// <param name="LastWriteTimeUtcTicks">The captured last-write timestamp.</param>
internal sealed record EntryCleanupCandidate(
    string Path,
    string LockPath,
    long Length,
    long LastWriteTimeUtcTicks)
    : CleanupCandidate(Path, Length, LastWriteTimeUtcTicks);
