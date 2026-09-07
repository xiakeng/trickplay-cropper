using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

using static Jellyfin.Plugin.TrickplayCropper.Jellyfin.JellyfinPreviewContextResolver;

namespace Jellyfin.Plugin.TrickplayCropper.Api;

internal static class TrickplayFrameProbeAuthorization
{
    private const string AuthenticationScheme = "CustomAuthentication";

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
        string? isApiKeyClaim = principal.FindFirst(JellyfinIsApiKeyClaim)?.Value;
        if (bool.TryParse(isApiKeyClaim, out bool isApiKey) && isApiKey)
        {
            return true;
        }

        string? userIdClaim = principal.FindFirst(JellyfinUserIdClaim)?.Value;
        return Guid.TryParse(userIdClaim, out Guid userId) && userId != Guid.Empty;
    }
}
