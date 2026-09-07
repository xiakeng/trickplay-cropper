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
        scenario.RecordAuthenticationAttempt();
        AuthenticateResult result = scenario.Authentication switch
        {
            AuthenticationState.UserSession => CreateUserSessionResult(scenario.UserId),
            AuthenticationState.ApiKeyWithoutCurrentUser => CreateApiKeyResult(),
            AuthenticationState.UserSessionWithoutUserId => CreateAuthenticatedResult(
                CreateNativeClaims(null, bool.FalseString)),
            AuthenticationState.UserSessionWithEmptyUserId => CreateAuthenticatedResult(
                CreateNativeClaims(string.Empty, bool.FalseString)),
            AuthenticationState.UserSessionWithEmptyGuid => CreateAuthenticatedResult(
                CreateNativeClaims(Guid.Empty.ToString("N"), bool.FalseString)),
            AuthenticationState.UserSessionWithMalformedUserId => CreateAuthenticatedResult(
                CreateNativeClaims("not-a-guid", bool.FalseString)),
            AuthenticationState.MalformedApiKeyWithoutCurrentUser => CreateAuthenticatedResult(
                CreateNativeClaims(Guid.Empty.ToString("N"), "not-a-boolean")),
            AuthenticationState.UnrelatedIdentity => AuthenticateResult.NoResult(),
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
        return CreateAuthenticatedResult(CreateNativeClaims(Guid.Empty.ToString("N"), bool.TrueString));
    }

    private static Claim[] CreateNativeClaims(string? userId, string isApiKey)
    {
        List<Claim> claims = [new Claim(IsApiKeyClaim, isApiKey)];
        if (userId is not null)
        {
            claims.Add(new Claim(UserIdClaim, userId));
        }

        return claims.ToArray();
    }

    private static AuthenticateResult CreateAuthenticatedResult(Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
