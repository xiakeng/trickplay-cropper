using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Jellyfin.Plugin.TrickplayCropper.Api;

internal static class TrickplayFrameProbeAuthorization
{
    private const string AuthenticationScheme = "CustomAuthentication";
    private const string IsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string UserIdClaim = "Jellyfin-UserId";

    public const string PolicyName = "TrickplayFrameProbe";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(PolicyName, policy =>
        {
            policy.AuthenticationSchemes.Add(AuthenticationScheme);
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => HasNativeIdentity(context.User));
        });
    }

    private static bool HasNativeIdentity(ClaimsPrincipal principal)
    {
        string? isApiKeyClaim = principal.FindFirst(IsApiKeyClaim)?.Value;
        if (bool.TryParse(isApiKeyClaim, out bool isApiKey) && isApiKey)
        {
            return true;
        }

        string? userIdClaim = principal.FindFirst(UserIdClaim)?.Value;
        return Guid.TryParse(userIdClaim, out Guid userId) && userId != Guid.Empty;
    }
}
