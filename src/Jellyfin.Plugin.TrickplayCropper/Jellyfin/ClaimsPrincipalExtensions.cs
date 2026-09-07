using System.Security.Claims;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>Reads Jellyfin's native identity claims without host lookups.</summary>
internal static class ClaimsPrincipalExtensions
{
    private const string IsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string UserIdClaim = "Jellyfin-UserId";

    /// <summary>Returns whether Jellyfin authenticated the identity as an API key.</summary>
    public static bool IsJellyfinApiKey(this ClaimsPrincipal principal)
    {
        Claim? claim = principal.Claims.FirstOrDefault(
            candidate => candidate.Type.Equals(IsApiKeyClaim, StringComparison.OrdinalIgnoreCase));
        return bool.TryParse(claim?.Value, out bool isApiKey) && isApiKey;
    }

    /// <summary>Reads a non-empty Jellyfin user identifier from the native identity.</summary>
    public static bool TryGetJellyfinUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        Claim? claim = principal.Claims.FirstOrDefault(
            candidate => candidate.Type.Equals(UserIdClaim, StringComparison.OrdinalIgnoreCase));
        return Guid.TryParse(claim?.Value, out userId) && userId != Guid.Empty;
    }
}
