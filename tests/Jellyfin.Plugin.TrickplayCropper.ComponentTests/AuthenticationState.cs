namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum AuthenticationState
{
    UserSession,
    ApiKeyWithoutCurrentUser,
    FalseApiKeyWithMissingUserId,
    MalformedApiKeyWithoutCurrentUser,
    MalformedApiKeyWithValidUserId,
    EmptyUserId,
    MalformedUserId,
    UnrelatedIdentity,
    Missing,
    Invalid,
    UnusableUserSession,
}
