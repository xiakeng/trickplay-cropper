namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Clears plugin-owned Preview Cache Entries for Jellyfin maintenance tasks.
/// </summary>
public interface IPreviewCacheMaintenance
{
    /// <summary>
    /// Clears eligible plugin-owned files while reporting progress.
    /// </summary>
    /// <param name="progress">The cleanup progress receiver.</param>
    /// <param name="cancellationToken">The cleanup cancellation token.</param>
    /// <returns>A task representing cleanup completion.</returns>
    Task ClearAsync(IProgress<double> progress, CancellationToken cancellationToken);
}
