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
}
