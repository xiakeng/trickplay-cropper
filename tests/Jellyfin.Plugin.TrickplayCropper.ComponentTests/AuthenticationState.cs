namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum AuthenticationState
{
    UserSession,
    ApiKeyWithoutCurrentUser,
    FalseApiKeyWithoutCurrentUser,
    MalformedApiKeyWithoutCurrentUser,
    MalformedApiKeyWithValidUserId,
    MissingUserId,
    EmptyUserId,
    MalformedUserId,
    UnrelatedIdentity,
    Missing,
    Invalid,
    UnusableUserSession,
}
