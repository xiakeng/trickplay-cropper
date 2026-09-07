using System.Security.Claims;

namespace Jellyfin.Plugin.TrickplayCropper.Jellyfin;

/// <summary>
/// Parses the native Jellyfin identity facts shared by GET and Trickplay Frame Probe authorization.
/// </summary>
internal sealed class JellyfinNativeIdentityClaims
{
    private const string IsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string UserIdClaim = "Jellyfin-UserId";

    /// <summary>Parses the native Jellyfin identity claims without loading a user.</summary>
    /// <param name="principal">The request-local principal produced by authentication.</param>
    /// <returns>The parsed native identity facts.</returns>
    internal static JellyfinNativeIdentityClaims Parse(ClaimsPrincipal principal)
    {
        Claim? apiKeyClaim = FindClaim(principal, IsApiKeyClaim);
        bool isApiKey = bool.TryParse(apiKeyClaim?.Value, out bool parsedApiKey) && parsedApiKey;
        Claim? userIdClaim = FindClaim(principal, UserIdClaim);
        Guid? userId = Guid.TryParse(userIdClaim?.Value, out Guid parsedUserId) && parsedUserId != Guid.Empty
            ? parsedUserId
            : null;
        return new JellyfinNativeIdentityClaims(isApiKey, userId);
    }

    private JellyfinNativeIdentityClaims(bool isApiKey, Guid? userId)
    {
        IsApiKey = isApiKey;
        UserId = userId;
    }

    /// <summary>Gets a value indicating whether native authentication resolved a Boolean true API-key claim.</summary>
    internal bool IsApiKey { get; }

    /// <summary>Gets the nonempty native user identifier, or <see langword="null"/> when none is valid.</summary>
    internal Guid? UserId { get; }

    /// <summary>Gets a value indicating whether the claims identify a resolved user or userless API key.</summary>
    internal bool HasUsableIdentity => IsApiKey || UserId is not null;

    private static Claim? FindClaim(ClaimsPrincipal principal, string claimType)
    {
        return principal.Claims.FirstOrDefault(
            claim => claim.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase));
    }
}
