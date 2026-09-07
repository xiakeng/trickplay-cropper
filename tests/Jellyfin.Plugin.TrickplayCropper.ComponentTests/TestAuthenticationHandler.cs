using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jellyfin.Plugin.TrickplayCropper.ComponentTests;

internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string IsApiKeyClaim = "Jellyfin-IsApiKey";
    private const string UserIdClaim = "Jellyfin-UserId";

    public const string SchemeName = "CustomAuthentication";

    private readonly PreviewScenario scenario;

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        PreviewScenario scenario)
        : base(options, logger, encoder)
    {
        this.scenario = scenario;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        AuthenticateResult result = scenario.Authentication switch
        {
            AuthenticationState.UserSession => CreateUserSessionResult(scenario.UserId),
            AuthenticationState.ApiKeyWithoutCurrentUser => CreateApiKeyResult(),
            AuthenticationState.FalseApiKeyWithMissingUserId => CreateClaimResult(IsApiKeyClaim, bool.FalseString),
            AuthenticationState.MalformedApiKeyWithoutCurrentUser => CreateClaimResult(IsApiKeyClaim, "not-a-Boolean"),
            AuthenticationState.MalformedApiKeyWithValidUserId => CreateMalformedApiKeyUserSessionResult(scenario.UserId),
            AuthenticationState.EmptyUserId => CreateUserSessionResult(Guid.Empty),
            AuthenticationState.MalformedUserId => CreateClaimResult(UserIdClaim, "not-a-Guid"),
            AuthenticationState.UnrelatedIdentity => CreateClaimResult(ClaimTypes.NameIdentifier, "unrelated-user"),
            AuthenticationState.Missing => AuthenticateResult.NoResult(),
            AuthenticationState.Invalid => AuthenticateResult.Fail("The component-test session is invalid."),
            AuthenticationState.UnusableUserSession => AuthenticateResult.Fail(
                "The component-test session is no longer usable."),
            _ => throw new InvalidOperationException("Unknown authentication scenario."),
        };
        return Task.FromResult(result);
    }

    private static AuthenticateResult CreateUserSessionResult(Guid authenticatedUserId)
    {
        Claim[] claims =
        [
            new Claim(UserIdClaim, authenticatedUserId.ToString("N")),
            new Claim(IsApiKeyClaim, bool.FalseString),
        ];
        return CreateAuthenticatedResult(claims);
    }

    private static AuthenticateResult CreateApiKeyResult()
    {
        Claim[] claims =
        [
            new Claim(UserIdClaim, Guid.Empty.ToString("N")),
            new Claim(IsApiKeyClaim, bool.TrueString),
        ];
        return CreateAuthenticatedResult(claims);
    }

    private static AuthenticateResult CreateClaimResult(string claimType, string value)
    {
        return CreateAuthenticatedResult([new Claim(claimType, value)]);
    }

    private static AuthenticateResult CreateMalformedApiKeyUserSessionResult(Guid authenticatedUserId)
    {
        Claim[] claims =
        [
            new Claim(UserIdClaim, authenticatedUserId.ToString("N")),
            new Claim(IsApiKeyClaim, "not-a-Boolean"),
        ];
        return CreateAuthenticatedResult(claims);
    }

    private static AuthenticateResult CreateAuthenticatedResult(Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
