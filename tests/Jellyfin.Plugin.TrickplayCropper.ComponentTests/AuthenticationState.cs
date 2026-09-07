namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum AuthenticationState
{
    UserSession,
    ApiKeyWithoutCurrentUser,
    UserSessionWithoutUserIdClaim,
    UserSessionWithEmptyUserIdClaim,
    UserSessionWithMalformedUserIdClaim,
    SessionWithoutUserAndFalseApiKeyClaim,
    SessionWithoutUserAndMalformedApiKeyClaim,
    UnrelatedIdentity,
    Missing,
    Invalid,
    UnusableUserSession,
}
