# Jellyfin 10.11.11 HEAD Authentication Reuse

## Scope and result

Research for [Verify Jellyfin native authentication reuse and HEAD security guarantees](https://github.com/xiakeng/trickplay-cropper/issues/101).
This is source analysis for v3 planning, not an implemented authorization change or a performance measurement.

The proposed named HEAD policy can remove the user lookup performed by Jellyfin's
`DefaultAuthorizationHandler`. Native authentication already rejects a token it
cannot resolve and an existing user marked disabled. It is **not** equivalent to
the current default policy: it drops per-user remote-access and parental-schedule
checks, and also accepts an authenticated non-API-key identity whose user was not
resolved. Those differences require an explicit HEAD contract decision before
implementation. The userless API-key case is separate and remains supported.
[Native authentication][auth-service] [Default handler][default-handler]
[Token resolution][auth-context] [Identity construction][custom-handler]

## Pinned evidence

- Jellyfin `v10.11.11`: `1fbd8739292cce610231be93daf43368733edf63`, resolved
  through the official [tag reference][jellyfin-tag].
- ASP.NET Core `v9.0.11`: `d3aba8fe1a0d0f5c145506f292b72ea9d28406fc`, resolved
  through the official [tag reference][aspnet-tag]. This is the .NET 9 reference
  implementation used here, not a claim about the runtime installed on a live
  server. Jellyfin's [SDK selection][sdk] permits roll-forward.
- Plugin baseline: `4ac1c0cefff520eb956e4dec1d4a83e7258fbe23` from fetched
  `origin/main`. The [package declarations][packages] pin Jellyfin 10.11.11.

All runtime conclusions below are inferences from these sources. No server was
started, restarted, deployed to, or queried for this investigation. No credential,
user, policy, or host configuration was created, revoked, or changed.

## Request execution and the two user loads

1. Jellyfin registers `CustomAuthentication` as its default authentication scheme
   and maps it to `CustomAuthenticationHandler`. Its default authorization policy
   selects that scheme and adds `DefaultAuthorizationRequirement`; the latter
   validates parental schedules by default. [Registration][registration]
   [Requirement][default-requirement]
2. `Startup.Configure` runs authentication, then routing, then authorization.
   The host's IP-based access middleware still runs after authorization, so
   dropping a user-specific authorization requirement does not remove server-wide
   network filtering. [Startup][startup] [IP filtering][ip-filter]
3. For a device/session token, `AuthorizationContext` matches the token to a
   device and calls `IUserManager.GetUserById(device.UserId)`. This is load A.
   API keys use a separate database lookup and no user load at this point.
   `UserManager.GetUserById` creates a database context and queries a user with
   permissions, preferences, schedules, and profile image; it does not return a
   process-wide cached user. [Token resolution][auth-context] [User lookup][user-load]
4. `AuthService` checks the token's authenticated state and the loaded user's
   disabled flag. `CustomAuthenticationHandler` turns the accepted information
   into a principal with an authenticated identity. [Native authentication][auth-service]
   [Identity construction][custom-handler]
5. For a non-API-key principal with a nonempty user ID,
   `DefaultAuthorizationHandler` calls `GetUserById` again. This is load B, used
   for user remote access and parental schedules. API keys succeed before this
   lookup. Removing this requirement removes load B; it does not remove load A.
   [Default handler][default-handler]

The two loads are an authentication lookup plus an authorization lookup, not two
executions of native authentication. This distinction matters to validation:
counting `AuthenticateAsync` calls alone cannot establish that load B disappeared.

## Why selecting the same scheme reuses authentication

`AuthenticationMiddleware` authenticates the default scheme and installs its
principal. `PolicyEvaluator` subsequently calls `AuthenticateAsync` for explicitly
selected policy schemes. [Authentication middleware][authentication-middleware]
[Policy evaluation][policy-evaluator]

The standard handler provider is scoped and keeps one initialized handler per
scheme in the request. `AuthenticationHandler.HandleAuthenticateOnceAsync` stores
the authentication task; later authentication calls on that handler return the
same result instead of executing `HandleAuthenticateAsync` again. The named policy
therefore reuses Jellyfin's original authentication work in the stock pipeline,
even though policy evaluation invokes the authentication API again.
[Scoped services][authentication-services] [Handler provider][handler-provider]
[Handler result reuse][handler-reuse]

This is request-local reuse, not a credential cache across requests. It also does
not rely on Jellyfin's `HttpContext.Items["AuthorizationInfo"]` cache:
`AuthService` calls the `HttpRequest` overload, which bypasses that cached
`HttpContext` overload. [Authorization overloads][authorization-overloads]

`RequireAuthenticatedUser` checks for an authenticated identity; it neither loads
a Jellyfin user nor validates a Jellyfin user ID. A policy tied to the explicit
native scheme does not merely trust an arbitrary pre-existing `HttpContext.User`:
`PolicyEvaluator` builds its principal from successful selected schemes and clears
it when none succeed. [Authenticated-identity requirement][deny-anonymous]
[Policy evaluation][policy-evaluator]

## Credential and user-state outcomes

The following outcomes assume the stock pinned pipeline, a fresh request, and
completed state changes through native operations. They describe the authorization
boundary, before the Trickplay Frame Probe validates its query and source facts.

| Case | Proposed authentication-only HEAD boundary | Evidence |
| --- | --- | --- |
| No token or unresolvable token | Does not enter the action; authentication yields no result or failure, leading to a challenge (normally 401). | [Auth service][auth-service], [custom handler][custom-handler], [policy evaluator][policy-evaluator], [challenge][handler-challenge] |
| Existing user whose disabled permission is true | Authentication fails before policy evaluation; normally 401. | [Auth service][auth-service] |
| Valid enabled user/session | Authenticated identity satisfies the named requirement. | [Custom handler][custom-handler], [requirement][deny-anonymous] |
| Valid API key without a current user | Authenticates with API-key=true, empty user ID, and administrator role; passes. A user's disabled flag cannot govern a key with no associated user. | [API-key lookup][api-key-lookup], [custom handler][custom-handler] |
| Session token after native logout or user-token revocation completes | The device token is removed from the in-memory device collection and database; a later authentication cannot resolve it as a session. It fails if it also has no independent API-key match. | [Logout/revoke][logout], [device deletion][device-delete] |
| API key after native deletion completes | The matching database key is deleted; the next lookup fails if there is no device token match. | [Key deletion][key-delete], [API-key lookup][api-key-lookup] |

Revocation is not a promise to cancel an already authenticated request. A request
already holding a device reference or an authentication result can outlive a
concurrent logout. Device lookup uses an in-memory collection, and authentication
can update the device's activity/version data; these operations are not an atomic
revocation fence. Request-result reuse does not add a cross-request cache, but
source inspection alone does not prove linearizable revocation under races.
[Device lookup/update/delete][devices] [Handler result reuse][handler-reuse]

### Non-API-key identity without a resolved user

The token resolver can mark a matched device authenticated and then receive null
from `GetUserById`. `AuthorizationInfo.UserId` becomes `Guid.Empty`. Native
identity creation still succeeds: its disabled check is null-conditional.
The default handler leaves its requirement unsatisfied for this non-API-key,
empty-user-ID principal, whereas `RequireAuthenticatedUser` alone accepts it.
[Token resolution][auth-context] [User ID derivation][authorization-info]
[Native authentication][auth-service] [Default handler][default-handler]

This is a source-visible branch, not an observed fixture or a claim that ordinary
user deletion always creates stale sessions. The official delete-user endpoint
revokes that user's tokens before deleting the user. Concurrent requests and
inconsistent state still need an explicit contract. [Delete-user ordering][delete-user]

A possible zero-additional-user-read guard is to require a native authenticated
principal and either the native API-key claim is true or the native user-ID claim
parses as a nonempty GUID. The native claims distinguish a userless API key from
an orphan session. This guards the unresolved-user result captured during
initial authentication; it does not recheck a user deleted afterward or make
revocation atomic. Treat it as a design option, not an approved change. If v3's
“invalid credential” promise includes this branch, the two-line policy alone is
insufficient. [Native claim construction][custom-handler]

## Authorization differences that need a decision

For an existing user, removing the default requirement drops the user-specific
remote-access check (including for administrators) and the parental-schedule
check (non-administrators). It also removes the second lookup's missing-user
failure if deletion occurs between the two loads. Retaining these semantics
would require a different design; simply retaining the default requirement also
retains load B. [Default handler][default-handler]

Server-wide network rules remain independently enforced. The planning ticket must
state whether HEAD is intentionally allowed for users denied by the two removed
per-user restrictions. GET must keep the default policy and all its current
application authorization. HEAD success still proves no visibility or playback
permission. [IP filtering][ip-filter] [Existing operation split][operation-split]

The current glossary and ADR define the Trickplay Frame Probe as passing the
ordinary endpoint policy. The proposed policy contradicts that existing wording;
resolve the change during requirements analysis before implementation.
[Current glossary][glossary] [Existing operation split][operation-split]

## Plugin wiring and test implications

The baseline controller has a controller-level `[Authorize]` and separate GET and
HEAD actions; the plugin registrator does not add a named authorization policy.
A named HEAD policy can be registered through
`Configure<AuthorizationOptions>` without replacing the host's default policy or
re-registering authentication. Move bare `[Authorize]` to GET, and put the named
policy on HEAD. [Controller][controller] [Registrator][registrator]

ASP.NET Core combines authorization metadata: a named policy does not override a
separate bare `[Authorize]`. Leaving the latter on the controller combines the
default requirement back into HEAD. Verify the resulting endpoint policy, not
just attribute presence, and ensure inherited/global metadata has not restored
the requirement. [Policy combination][policy-combination]

The current component fixture uses its own `ComponentTest` authentication scheme
and test default requirement. Its invalid/unusable-session outcomes are scripted
failures; these do not prove native revocation or disabled-user handling. Tests
will need deliberate support for the native scheme name when production adds the
named policy, plus independent coverage of policy composition and native claims.
[Component authentication fixture][component-fixture]

Suggested evidence for the implementation phase, subject to the decision ticket:

- Effective HEAD policy excludes `DefaultAuthorizationRequirement`; effective GET
  policy retains it. GET's current authorization and representation tests pass.
- Same-request authentication work is executed once; a user-bound HEAD performs
  load A but no default-handler load B. An API-key run cannot demonstrate a
  two-to-one user-load improvement because it bypasses both loads already.
- Real Jellyfin integration checks valid user sessions, valid userless API keys,
  missing/invalid credentials, completed session and key revocation, and an
  existing disabled user, with authorized fixture creation and restoration.
- The decided remote-access, schedule, and orphan-session behaviors receive
  explicit coverage. A constructed orphan principal proves the policy boundary;
  it does not by itself prove a native host can reach that state normally.

These are recommendations for later implementation, not tests executed here.

## Response-time evidence required before acceptance

The baseline integration harness already records complete buffered client HTTP
response timings for HEAD and GET, with counts, min, max, median, and mean in a
Markdown report. It does not currently establish a paired before/after comparison
or native authentication load counts. [Scrub Storm report][scrub-report]

Jellyfin's `X-Response-Time-ms` header measures from its response-time middleware
entry to `OnStarting`, when response headers begin. This differs from the
harness's complete client request duration. Preserve that distinction when
recording both metrics. [Host response timing][response-time]

The user-reported 94%-97% share is an input to this investigation, not a measured
result reproduced here. Source analysis predicts less database work; it does not
establish an actual latency reduction or its size. Acceptance needs integration
reports that visibly compare baseline and candidate HEAD response time on the
same host/runtime, user-bound credential, fixture, request mix, concurrency,
warm-up, and cache conditions. Record exact plugin commits and runtime versions,
sample counts, failures, and distributions; agree the comparison and noise rule
in the performance decision ticket. Keep GET as an unchanged control. A successful
build or a mock timing test cannot satisfy the required visible decrease.

## Remaining decisions

1. Approve or reject removing user remote-access and parental-schedule enforcement
   from HEAD, while retaining host network filtering and all GET behavior.
2. Define invalid/unusable-session scope, particularly orphan native identities;
   choose bare authentication-only policy or an additional claim-based guard.
3. Define the integration comparison, repeatability threshold, fixture management,
   and evidence required to demonstrate lower HEAD response time safely.

## Sources

[jellyfin-tag]: https://api.github.com/repos/jellyfin/jellyfin/git/ref/tags/v10.11.11
[aspnet-tag]: https://api.github.com/repos/dotnet/aspnetcore/git/ref/tags/v9.0.11
[sdk]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/global.json
[packages]: https://github.com/xiakeng/trickplay-cropper/blob/4ac1c0cefff520eb956e4dec1d4a83e7258fbe23/Directory.Packages.props
[registration]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs#L56-L105
[default-requirement]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationRequirement.cs
[startup]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server/Startup.cs#L208-L216
[ip-filter]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Middleware/IpBasedAccessValidationMiddleware.cs#L36-L62
[auth-context]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs#L112-L192
[api-key-lookup]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs#L193-L222
[authorization-overloads]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs#L43-L70
[user-load]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Users/UserManager.cs#L123-L144
[auth-service]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Emby.Server.Implementations/HttpServer/Security/AuthService.cs#L21-L38
[custom-handler]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Auth/CustomAuthenticationHandler.cs#L43-L88
[default-handler]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs#L41-L97
[authorization-info]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Net/AuthorizationInfo.cs#L16
[delete-user]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/UserController.cs#L155-L166
[logout]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Emby.Server.Implementations/Session/SessionManager.cs#L1685-L1747
[device-delete]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Devices/DeviceManager.cs#L210-L219
[devices]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Devices/DeviceManager.cs#L146-L232
[key-delete]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Security/AuthenticationManager.cs#L57-L68
[authentication-middleware]: https://github.com/dotnet/aspnetcore/blob/d3aba8fe1a0d0f5c145506f292b72ea9d28406fc/src/Security/Authentication/Core/src/AuthenticationMiddleware.cs#L59-L77
[policy-evaluator]: https://github.com/dotnet/aspnetcore/blob/d3aba8fe1a0d0f5c145506f292b72ea9d28406fc/src/Security/Authorization/Policy/src/PolicyEvaluator.cs#L31-L107
[authentication-services]: https://github.com/dotnet/aspnetcore/blob/d3aba8fe1a0d0f5c145506f292b72ea9d28406fc/src/Http/Authentication.Core/src/AuthenticationCoreServiceCollectionExtensions.cs#L23-L26
[handler-provider]: https://github.com/dotnet/aspnetcore/blob/d3aba8fe1a0d0f5c145506f292b72ea9d28406fc/src/Http/Authentication.Core/src/AuthenticationHandlerProvider.cs#L28-L57
[handler-reuse]: https://github.com/dotnet/aspnetcore/blob/d3aba8fe1a0d0f5c145506f292b72ea9d28406fc/src/Security/Authentication/Core/src/AuthenticationHandler.cs#L215-L255
[handler-challenge]: https://github.com/dotnet/aspnetcore/blob/d3aba8fe1a0d0f5c145506f292b72ea9d28406fc/src/Security/Authentication/Core/src/AuthenticationHandler.cs#L299-L303
[deny-anonymous]: https://github.com/dotnet/aspnetcore/blob/d3aba8fe1a0d0f5c145506f292b72ea9d28406fc/src/Security/Authorization/Core/src/DenyAnonymousAuthorizationRequirement.cs#L22-L33
[policy-combination]: https://github.com/dotnet/aspnetcore/blob/d3aba8fe1a0d0f5c145506f292b72ea9d28406fc/src/Security/Authorization/Core/src/AuthorizationPolicy.cs#L130-L198
[controller]: https://github.com/xiakeng/trickplay-cropper/blob/4ac1c0cefff520eb956e4dec1d4a83e7258fbe23/src/Jellyfin.Plugin.TrickplayCropper/Api/TrickplayPreviewController.cs#L14-L81
[registrator]: https://github.com/xiakeng/trickplay-cropper/blob/4ac1c0cefff520eb956e4dec1d4a83e7258fbe23/src/Jellyfin.Plugin.TrickplayCropper/PluginServiceRegistrator.cs
[component-fixture]: https://github.com/xiakeng/trickplay-cropper/blob/4ac1c0cefff520eb956e4dec1d4a83e7258fbe23/tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests/TrickplayPreviewHttpSpecs.cs#L2667-L2682
[glossary]: https://github.com/xiakeng/trickplay-cropper/blob/4ac1c0cefff520eb956e4dec1d4a83e7258fbe23/CONTEXT.md
[operation-split]: https://github.com/xiakeng/trickplay-cropper/blob/4ac1c0cefff520eb956e4dec1d4a83e7258fbe23/docs/adr/0004-separate-frame-probe-calculation-from-preview-authorization.md
[scrub-report]: https://github.com/xiakeng/trickplay-cropper/blob/4ac1c0cefff520eb956e4dec1d4a83e7258fbe23/tools/TrickplayCropper.IntegrationHarness/ScrubStormReport.cs#L36-L87
[response-time]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Middleware/ResponseTimeMiddleware.cs#L41-L66
