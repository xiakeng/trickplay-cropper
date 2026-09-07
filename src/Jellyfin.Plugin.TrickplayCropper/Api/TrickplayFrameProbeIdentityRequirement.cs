using Jellyfin.Plugin.TrickplayCropper.Jellyfin;
using Microsoft.AspNetCore.Authorization;

namespace Jellyfin.Plugin.TrickplayCropper.Api;

/// <summary>
/// Requires a request-local Jellyfin identity with a resolved user or a userless API key.
/// </summary>
internal sealed class TrickplayFrameProbeIdentityRequirement
    : AuthorizationHandler<TrickplayFrameProbeIdentityRequirement>, IAuthorizationRequirement
{
    /// <summary>The Jellyfin authentication scheme selected by the Frame Probe policy.</summary>
    internal const string AuthenticationScheme = "CustomAuthentication";

    /// <summary>The authorization policy applied only to Trickplay Frame Probe requests.</summary>
    internal const string PolicyName = "TrickplayFrameProbe";

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TrickplayFrameProbeIdentityRequirement requirement)
    {
        JellyfinNativeIdentityClaims claims = JellyfinNativeIdentityClaims.Parse(context.User);
        if (claims.HasUsableIdentity)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
