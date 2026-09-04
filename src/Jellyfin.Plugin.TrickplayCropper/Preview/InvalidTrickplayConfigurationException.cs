using System.Globalization;

namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Reports one deterministic failure in Jellyfin's current Trickplay configuration snapshot.
/// </summary>
internal sealed class InvalidTrickplayConfigurationException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTrickplayConfigurationException"/> class.
    /// </summary>
    /// <param name="failedValidation">The stable name of the failed validation.</param>
    /// <param name="failedValue">The value that failed validation.</param>
    public InvalidTrickplayConfigurationException(string failedValidation, long failedValue)
        : base(string.Create(
            CultureInfo.InvariantCulture,
            $"Jellyfin trickplay configuration failed validation {failedValidation} for value {failedValue}."))
    {
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
}
