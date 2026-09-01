namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Preserves redaction-safe diagnostics across an internal Preview processing boundary.
/// </summary>
internal sealed class PreviewStageException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PreviewStageException"/> class.
    /// </summary>
    /// <param name="cause">The internal failure being transported.</param>
    /// <param name="details">The redaction-safe values available at failure time.</param>
    public PreviewStageException(Exception cause, PreviewFailureDetails details)
        : base(cause.Message, cause)
    {
        CauseType = cause.GetType().Name;
        Details = details;
    }

    /// <summary>
    /// Gets the redaction-safe failure type name.
    /// </summary>
    public string CauseType { get; }

    /// <summary>
    /// Gets the redaction-safe values available at failure time.
    /// </summary>
    public PreviewFailureDetails Details { get; }
}
