namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum ConfigurationFailureKind
{
    UnreadableSnapshot,
    NonPositiveConfiguredTarget,
    NonPositiveSelectedResolution,
}
