namespace Jellyfin.Plugin.TrickplayCropper.Caching;

/// <summary>
/// Holds one cleanup run's fixed boundary, cancellation, diagnostics, and counters.
/// </summary>
internal sealed class CleanupRunContext
{
    private readonly HashSet<string> warnedReparsePoints = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupRunContext"/> class.
    /// </summary>
    /// <param name="cleanupStartedUtc">The fixed cleanup boundary.</param>
    /// <param name="counters">The run's mutable counters.</param>
    /// <param name="cancellationToken">The run cancellation token.</param>
    public CleanupRunContext(
        DateTime cleanupStartedUtc,
        CleanupCounters counters,
        CancellationToken cancellationToken)
    {
        CleanupStartedUtc = cleanupStartedUtc;
        Counters = counters;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the run cancellation token.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Gets the fixed cleanup boundary.</summary>
    public DateTime CleanupStartedUtc { get; }

    /// <summary>Gets the run's mutable counters.</summary>
    public CleanupCounters Counters { get; }

    /// <summary>Records a reparse point once for the current cleanup run.</summary>
    /// <param name="path">The reparse-point path.</param>
    /// <returns><see langword="true"/> when this is the first observation.</returns>
    public bool TryRecordReparsePoint(string path)
    {
        return warnedReparsePoints.Add(path);
    }
}
