using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Jellyfin.Plugin.TrickplayCropper.Api;

internal static class TrickplayFrameProbeAuthorizationPolicy
{
    private const string JellyfinIsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string JellyfinUserIdClaim = "Jellyfin-UserId";
    private const string AuthenticationScheme = "CustomAuthentication";

    public const string Name = "TrickplayFrameProbe";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(
            Name,
            policy => policy
                .AddAuthenticationSchemes(AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => HasNativeIdentity(context.User)));
    }

    private static bool HasNativeIdentity(ClaimsPrincipal principal)
    {
        Claim? apiKeyClaim = principal.Claims.FirstOrDefault(
            claim => claim.Type.Equals(JellyfinIsApiKeyClaim, StringComparison.OrdinalIgnoreCase));
        if (bool.TryParse(apiKeyClaim?.Value, out bool isApiKey) && isApiKey)
        {
            return true;
        }

        Claim? userIdClaim = principal.Claims.FirstOrDefault(
            claim => claim.Type.Equals(JellyfinUserIdClaim, StringComparison.OrdinalIgnoreCase));
        return Guid.TryParse(userIdClaim?.Value, out Guid userId) && userId != Guid.Empty;
    }
}
