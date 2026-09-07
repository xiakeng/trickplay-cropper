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
            AuthenticationState.UserSessionWithoutUserId => CreateAuthenticatedResult(
                [new Claim(IsApiKeyClaim, bool.FalseString)]),
            AuthenticationState.UserSessionWithEmptyUserId => CreateAuthenticatedResult(
                [new Claim(UserIdClaim, string.Empty), new Claim(IsApiKeyClaim, bool.FalseString)]),
            AuthenticationState.UserSessionWithMalformedUserId => CreateAuthenticatedResult(
                [new Claim(UserIdClaim, "not-a-guid"), new Claim(IsApiKeyClaim, bool.FalseString)]),
            AuthenticationState.UserSessionWithMalformedApiKeyClaim => CreateAuthenticatedResult(
                [new Claim(UserIdClaim, Guid.Empty.ToString("N")), new Claim(IsApiKeyClaim, "not-a-Boolean")]),
            AuthenticationState.UnrelatedIdentity => CreateAuthenticatedResult(
                [new Claim(ClaimTypes.NameIdentifier, scenario.UserId.ToString("N"))],
                "UnrelatedAuthentication"),
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

    private static AuthenticateResult CreateAuthenticatedResult(Claim[] claims)
    {
        return CreateAuthenticatedResult(claims, SchemeName);
    }

    private static AuthenticateResult CreateAuthenticatedResult(Claim[] claims, string authenticationType)
    {
        var identity = new ClaimsIdentity(claims, authenticationType);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
