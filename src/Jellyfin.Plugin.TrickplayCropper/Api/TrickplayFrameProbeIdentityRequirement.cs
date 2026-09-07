using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Jellyfin.Plugin.TrickplayCropper.Api;

internal sealed class TrickplayFrameProbeIdentityRequirement
    : AuthorizationHandler<TrickplayFrameProbeIdentityRequirement>, IAuthorizationRequirement
{
    private const string IsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string UserIdClaim = "Jellyfin-UserId";

    internal const string AuthenticationScheme = "CustomAuthentication";
    internal const string PolicyName = "TrickplayFrameProbe";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TrickplayFrameProbeIdentityRequirement requirement)
    {
        if (IsNativeIdentity(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool IsNativeIdentity(ClaimsPrincipal principal)
    {
        Claim? apiKeyClaim = FindClaim(principal, IsApiKeyClaim);
        if (bool.TryParse(apiKeyClaim?.Value, out bool isApiKey) && isApiKey)
        {
            return true;
        }

        Claim? userIdClaim = FindClaim(principal, UserIdClaim);
        return Guid.TryParse(userIdClaim?.Value, out Guid userId) && userId != Guid.Empty;
    }

    private static Claim? FindClaim(ClaimsPrincipal principal, string claimType)
    {
        return principal.Claims.FirstOrDefault(
            claim => claim.Type.Equals(claimType, StringComparison.OrdinalIgnoreCase));
    }
}
