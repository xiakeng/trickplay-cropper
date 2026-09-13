namespace TrickplayCropper.IntegrationHarness;

/// <summary>A direct Frame Index with its independent generated-metadata oracle.</summary>
internal sealed record PreviewRequest(Guid Item, int FrameIndex, PlaybackMetadata Metadata)
{
    /// <summary>Gets the expected Source Sprite index.</summary>
    public int SpriteIndex => FrameIndex / Metadata.FramesPerSprite;

    /// <summary>Gets the request route without credentials.</summary>
    public string Route => FormattableString.Invariant($"/TrickplayCropper/Videos/{Item:N}/Preview?FrameIndex={FrameIndex}");
}
