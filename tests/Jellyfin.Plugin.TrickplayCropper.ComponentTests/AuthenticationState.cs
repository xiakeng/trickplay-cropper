namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum AuthenticationState
{
    UserSession,
    ApiKeyWithoutCurrentUser,
    MissingUserId,
    EmptyUserId,
    MalformedUserId,
    FalseApiKeyWithoutCurrentUser,
    MalformedApiKeyWithoutCurrentUser,
    UnrelatedIdentity,
    Missing,
    Invalid,
    UnusableUserSession,
}
