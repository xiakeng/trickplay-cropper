namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Holds one cleanup run's fixed boundary, cancellation, diagnostics, and counters.
/// </summary>
internal sealed class CleanupRunContext
{
    private readonly HashSet<string> warnedReparsePoints = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public CleanupRunContext(
        DateTime cleanupStartedUtc,
        CleanupCounters counters,
        CancellationToken cancellationToken)
    {
        CleanupStartedUtc = cleanupStartedUtc;
        Counters = counters;
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }

    public DateTime CleanupStartedUtc { get; }

    public CleanupCounters Counters { get; }

    public bool TryRecordReparsePoint(string path)
    {
        return warnedReparsePoints.Add(path);
    }
}

internal sealed class CleanupCounters
{
    public int DeletedFiles { get; set; }

    public int FailedFiles { get; set; }

    public int DeletedDirectories { get; set; }

    public int FailedDirectories { get; set; }

    public int SkippedChangedFiles { get; set; }

    public bool Cancelled { get; set; }
}
