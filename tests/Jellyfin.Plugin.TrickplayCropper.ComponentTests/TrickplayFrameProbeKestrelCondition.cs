namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum TrickplayFrameProbeKestrelCondition
{
    Success,
    MalformedInput,
    UnauthenticatedSession,
    DefaultPolicyDenied,
    ApiKeyWithoutCurrentUser,
    ConcealedResource,
    InvalidMetadata,
}
