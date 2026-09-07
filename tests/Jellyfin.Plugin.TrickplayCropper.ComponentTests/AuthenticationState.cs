namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum AuthenticationState
{
    UserSession,
    ApiKeyWithoutCurrentUser,
    UserSessionWithoutUserId,
    UserSessionWithEmptyUserId,
    UserSessionWithMalformedUserId,
    UserSessionWithMalformedApiKeyClaim,
    UnrelatedIdentity,
    Missing,
    Invalid,
    UnusableUserSession,
}
