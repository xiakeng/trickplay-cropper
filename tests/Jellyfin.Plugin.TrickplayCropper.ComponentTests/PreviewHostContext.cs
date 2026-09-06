namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed record PreviewHostContext(
    string TemporaryDirectory,
    string SourceSpritePath,
    PreviewScenario Scenario);
