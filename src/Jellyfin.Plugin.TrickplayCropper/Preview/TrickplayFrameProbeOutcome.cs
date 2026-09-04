namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Defines every outcome produced by the Trickplay Frame Probe.
/// </summary>
public abstract record TrickplayFrameProbeOutcome
{
    /// <summary>
    /// Represents an authorized probe whose Frame Index is selected.
    /// </summary>
    /// <param name="FrameIndex">The clamped zero-based Frame Index.</param>
    public sealed record Success(int FrameIndex) : TrickplayFrameProbeOutcome;

    /// <summary>
    /// Represents an invalid request.
    /// </summary>
    public sealed record BadRequest : TrickplayFrameProbeOutcome;

    /// <summary>
    /// Represents a request without a usable authenticated user.
    /// </summary>
    public sealed record Unauthorized : TrickplayFrameProbeOutcome;

    /// <summary>
    /// Represents an authenticated request without user-scoped playback authority.
    /// </summary>
    public sealed record Forbidden : TrickplayFrameProbeOutcome;

    /// <summary>
    /// Represents an unavailable or concealed resource.
    /// </summary>
    public sealed record NotFound : TrickplayFrameProbeOutcome;

    /// <summary>
    /// Represents an unexpected probe-processing failure.
    /// </summary>
    public sealed record InternalError : TrickplayFrameProbeOutcome;
}
