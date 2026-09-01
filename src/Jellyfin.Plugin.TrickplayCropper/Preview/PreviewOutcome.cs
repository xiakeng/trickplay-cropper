namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Defines every outcome produced by the Trickplay Preview request module.
/// </summary>
public abstract record PreviewOutcome
{
    /// <summary>
    /// Represents a successful response with JPEG content.
    /// </summary>
    /// <param name="Content">The immutable response content.</param>
    /// <param name="EntityTag">The canonical entity tag.</param>
    /// <param name="Telemetry">The completed request telemetry.</param>
    public sealed record Ok(
        ReadOnlyMemory<byte> Content,
        string EntityTag,
        PreviewTelemetry Telemetry) : PreviewOutcome;

    /// <summary>
    /// Represents a conditional request whose entity is unchanged.
    /// </summary>
    /// <param name="EntityTag">The canonical entity tag.</param>
    /// <param name="Telemetry">The completed request telemetry.</param>
    public sealed record NotModified(string EntityTag, PreviewTelemetry Telemetry) : PreviewOutcome;

    /// <summary>
    /// Represents an invalid request.
    /// </summary>
    public sealed record BadRequest : PreviewOutcome;

    /// <summary>
    /// Represents a request without a usable authenticated user.
    /// </summary>
    public sealed record Unauthorized : PreviewOutcome;

    /// <summary>
    /// Represents an authenticated request without playback permission.
    /// </summary>
    public sealed record Forbidden : PreviewOutcome;

    /// <summary>
    /// Represents an unavailable or concealed resource.
    /// </summary>
    public sealed record NotFound : PreviewOutcome;

    /// <summary>
    /// Represents an unexpected request-processing failure.
    /// </summary>
    public sealed record InternalError : PreviewOutcome;
}
