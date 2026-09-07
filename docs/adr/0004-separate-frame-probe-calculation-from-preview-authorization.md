# Separate Frame Probe calculation from Preview authorization

The v3 authentication boundary below is implemented; its before/after response-time
acceptance remains pending.

GET and HEAD answer different questions and must not share an authorization context.
GET returns or revalidates a representation, so it resolves a current Jellyfin user,
conceals user-invisible Items, checks logical-video playback authority, and proves source
membership and Source Video visibility through user-scoped host APIs before any `200` or
`304`. HEAD answers only which generated Frame Index the requested position selects after
Jellyfin's native authentication accepts a user identity or userless API key. It therefore resolves exact Item
and Source Video identities without a user, proves membership through the full playback
Media Source enumeration with explicit media probing disabled, and makes no visibility or
playback decision. A userless API key may consequently receive a successful HEAD while
the corresponding GET remains forbidden. The two paths share one request-local target and
Frame Index calculation whose inputs contain no identity or authorization state.
Configuration remains request-local; generated metadata follows the bounded observation
policy in ADR 0005. Every successful GET `200` or `304` carries that final Frame Index in
`X-Trickplay-Frame-Index`, independent
of the representation ETag. This split keeps HEAD structurally unable to become permission
evidence while preserving one deterministic calculation for both operations.

For v3, register the named `TrickplayFrameProbe` policy through
`Configure<AuthorizationOptions>` without replacing Jellyfin's default policy or
re-registering its authentication scheme. HEAD uses that policy, selecting native
`CustomAuthentication`, requiring an
authenticated identity and either the native `Jellyfin-IsApiKey` claim set to true or a
`Jellyfin-UserId` claim parseable as a non-empty GUID. This claim-only guard rejects a
device/session identity whose user could not be resolved during authentication, without
another user lookup; it preserves legitimate userless API keys. Native authentication
continues to reject invalid or revoked credentials and disabled users. Reuse is confined
to the same request: this does not promise to cancel in-flight requests when credentials
are revoked or a user is deleted or disabled after authentication.

HEAD intentionally omits `DefaultAuthorizationRequirement`, removing its second user
lookup and its per-user remote-access and parental-schedule restrictions. This permits
Frame Index queries even when those restrictions deny GET; server-level network controls
remain in force. Move the controller-level default authorization metadata to GET so it
cannot combine with HEAD's named policy. GET retains its existing default policy and all
visibility, playback, response, and cache semantics. HEAD still avoids Source Sprite,
Cache Tree, and encoder access. This boundary trades the default policy's user restrictions
for a lighter authenticated calculation; a measured speedup still requires separate
integration acceptance evidence.

Keep the native challenge/forbid distinction: missing, invalid, or revoked credentials
and disabled-user authentication failures return `401`; an authenticated device/session
identity rejected by the claim-only guard returns `403`. HEAD remains bodyless, preserving
its existing successful response headers and `400`, `404`, and `500` semantics. This
contract does not introduce a custom mapping of guard failures to `401`.

Verification reuses the existing real integration tests: measure response time once
before the code changes and once after completion under comparable conditions, and
require a clearly visible decrease in HEAD response time. Do not add integration or
unit tests for timing acceptance, and do not add authentication scenarios to integration
tests. Authentication changes may add or update focused unit tests for native scheme
selection, effective HEAD/GET policy composition, claim validation, and challenge/forbid
behavior. Existing simulated assertions that depend on HEAD's old default policy must
be reconciled with this contract; they do not establish real Jellyfin revocation or
disabled-user behavior. The performance comparison is not evidence of an executed
native authentication security matrix. Detailed measurement reporting remains in
[Choose integration response-time reporting and HEAD improvement acceptance](https://github.com/xiakeng/trickplay-cropper/issues/105).
