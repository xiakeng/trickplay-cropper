using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed class TestDefaultAuthorizationHandler
    : AuthorizationHandler<TestDefaultAuthorizationRequirement>
{
    private readonly PreviewScenario scenario;

    public TestDefaultAuthorizationHandler(PreviewScenario scenario)
    {
        this.scenario = scenario;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TestDefaultAuthorizationRequirement requirement)
    {
        if (HasResolvedUser(context.User))
        {
            scenario.RecordDefaultAuthorizationUserLoad();
        }

        if (scenario.DeniesDefaultAuthorizationPolicy)
        {
            context.Fail();
        }
        else
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool HasResolvedUser(ClaimsPrincipal principal)
    {
        string? apiKey = principal.FindFirst("Jellyfin-IsApiKey")?.Value;
        string? userId = principal.FindFirst("Jellyfin-UserId")?.Value;
        return !(bool.TryParse(apiKey, out bool isApiKey) && isApiKey)
            && Guid.TryParse(userId, out Guid parsedUserId)
            && parsedUserId != Guid.Empty;
    }
}
