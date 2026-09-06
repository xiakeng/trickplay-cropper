namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Accumulates one cleanup run's observable outcomes.
/// </summary>
internal sealed class CleanupCounters
{
    /// <summary>Gets or sets the deleted file count.</summary>
    public int DeletedFiles { get; set; }

    /// <summary>Gets or sets the failed file count.</summary>
    public int FailedFiles { get; set; }

    /// <summary>Gets or sets the deleted directory count.</summary>
    public int DeletedDirectories { get; set; }

    /// <summary>Gets or sets the failed directory count.</summary>
    public int FailedDirectories { get; set; }

    /// <summary>Gets or sets the count of candidates that changed after capture.</summary>
    public int SkippedChangedFiles { get; set; }

    /// <summary>Gets or sets a value indicating whether the run was cancelled.</summary>
    public bool Cancelled { get; set; }
}
