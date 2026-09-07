namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

public enum AuthenticationState
{
    UserSession,
    ApiKeyWithoutCurrentUser,
    Missing,
    Invalid,
    UnusableUserSession,
    UnrelatedIdentity,
}
