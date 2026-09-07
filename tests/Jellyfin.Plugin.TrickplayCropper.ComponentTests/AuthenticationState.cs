namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum AuthenticationState
{
    UserSession,
    UserSessionWithMalformedApiKey,
    ApiKeyWithoutCurrentUser,
    UserlessFalseApiKey,
    UserlessMalformedApiKey,
    MissingUserId,
    EmptyUserId,
    MalformedUserId,
    UnrelatedIdentity,
    Missing,
    Invalid,
    UnusableUserSession,
}
