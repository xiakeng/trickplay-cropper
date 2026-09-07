namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum AuthenticationState
{
    UserSession,
    ApiKeyWithoutCurrentUser,
    UserSessionWithoutUserId,
    UserSessionWithEmptyUserId,
    UserSessionWithEmptyGuid,
    UserSessionWithMalformedUserId,
    MalformedApiKeyWithoutCurrentUser,
    UnrelatedIdentity,
    Missing,
    Invalid,
    UnusableUserSession,
}
