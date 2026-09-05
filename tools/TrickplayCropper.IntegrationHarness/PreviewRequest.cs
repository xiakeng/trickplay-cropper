namespace TrickplayCropper.IntegrationHarness;

/// <summary>A playback position with its independent generated-metadata oracle.</summary>
internal sealed record PreviewRequest(Guid Item, long Ticks, PlaybackMetadata Metadata)
{
    /// <summary>Gets the expected Frame Index.</summary>
    public int FrameIndex => Metadata.FrameIndex(Ticks);

    /// <summary>Gets the expected Source Sprite index.</summary>
    public int SpriteIndex => FrameIndex / Metadata.FramesPerSprite;

    /// <summary>Gets the request route without credentials.</summary>
    public string Route => FormattableString.Invariant($"/TrickplayCropper/Videos/{Item:N}/Preview?PositionTicks={Ticks}");
}
