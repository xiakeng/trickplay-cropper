namespace Jellyfin.Plugin.TrickplayCropper.Preview;

/// <summary>
/// Defines every outcome produced by the Trickplay Frame Probe.
/// </summary>
public abstract record TrickplayFrameProbeOutcome
{
    /// <summary>
    /// Represents an accepted probe whose Frame Index is selected.
    /// </summary>
    /// <param name="FrameIndex">The clamped zero-based Frame Index.</param>
    public sealed record Success(int FrameIndex) : TrickplayFrameProbeOutcome;

    /// <summary>
    /// Represents an invalid request.
    /// </summary>
    public sealed record BadRequest : TrickplayFrameProbeOutcome;

    /// <summary>
    /// Represents a retained transport-facing authentication refusal.
    /// </summary>
    public sealed record Unauthorized : TrickplayFrameProbeOutcome;

    /// <summary>
    /// Represents a retained transport-facing ordinary-policy refusal.
    /// </summary>
    public sealed record Forbidden : TrickplayFrameProbeOutcome;

    /// <summary>
    /// Represents unavailable user-independent source or calculation inputs.
    /// </summary>
    public sealed record NotFound : TrickplayFrameProbeOutcome;

    /// <summary>
    /// Represents an unexpected probe-processing failure.
    /// </summary>
    public sealed record InternalError : TrickplayFrameProbeOutcome;
}
