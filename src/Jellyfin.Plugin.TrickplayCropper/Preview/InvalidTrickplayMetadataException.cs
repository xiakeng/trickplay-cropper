using System.Globalization;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Reports one deterministic validation failure in trusted Jellyfin trickplay metadata.
/// </summary>
internal sealed class InvalidTrickplayMetadataException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTrickplayMetadataException"/> class.
    /// </summary>
    /// <param name="metadata">The metadata snapshot that failed validation.</param>
    /// <param name="failedValidation">The stable name of the failed validation.</param>
    /// <param name="failedValue">The value that failed validation.</param>
    public InvalidTrickplayMetadataException(
        TrickplayMetadata metadata,
        string failedValidation,
        long failedValue)
        : base(string.Create(
            CultureInfo.InvariantCulture,
            $"Jellyfin trickplay metadata failed validation {failedValidation} for value {failedValue}."))
    {
        Metadata = metadata;
        FailedValidation = failedValidation;
        FailedValue = failedValue;
    }

    /// <summary>
    /// Gets the stable name of the failed validation.
    /// </summary>
    public string FailedValidation { get; }

    /// <summary>
    /// Gets the value that failed validation.
    /// </summary>
    public long FailedValue { get; }

    /// <summary>
    /// Gets the metadata snapshot that failed validation.
    /// </summary>
    public TrickplayMetadata Metadata { get; }

    /// <summary>
    /// Gets the selection values computed before coordinate narrowing failed.
    /// </summary>
    public FrameSelectionDiagnostics? SelectionDiagnostics { get; init; }

    /// <summary>
    /// Gets or sets the configuration and normalization values known when the failure was raised.
    /// </summary>
    /// <remarks>
    /// The context resolver enriches this for metadata failures it raises so the diagnostic log can
    /// reconstruct the configuration, selection, and generated-key inputs. Failures raised during GET-only
    /// frame selection leave this null because those values are no longer in scope.
    /// </remarks>
    public PreviewConfigurationDiagnostics? Configuration { get; set; }
}
