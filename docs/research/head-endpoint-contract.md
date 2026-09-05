# Lightweight HEAD Endpoint Contract

## Answer

The existing Preview URI can support a lightweight probe with a dedicated
`[HttpHead]` action. ASP.NET Core 9 matches HTTP methods exactly: a HEAD request
does not fall back to the `[HttpGet]` action. The HEAD action must invoke a
separate probe service and stop after ordinary endpoint policy, user-independent
Item and Media Source membership, metadata lookup for an already determined
Selected Trickplay Resolution, and a calculation that produces only the Frame
Index. How that resolution is selected is a separate contract and outside this
document. The HEAD action must never
delegate to `GetAsync` or use the existing source resolver unchanged.

A successful response is `200 OK`, has no content, and has exactly these two
plugin-owned headers:

```http
X-Trickplay-Frame-Index: 42
Cache-Control: private, no-cache
```

It does not emit `ETag`, `Content-Type`, `Content-Disposition`,
`X-Trickplay-Cache`, or `Server-Timing`; it does not read `If-None-Match`; and it
must omit `Content-Length` rather than report zero. Server and middleware fields
such as `Date`, `Server`, `X-Response-Time-ms`, and applicable CORS fields can
still be present. HEAD success proves only that ordinary endpoint policy accepted
the request and the user-independent source facts map to a Frame Index. It does
not prove user visibility, playback permission, or that a Source Sprite or Preview
Cache Entry exists, so a subsequent GET can still fail.

## Routing and action selection

`HttpHeadAttribute` advertises only `HEAD`, while the endpoint matcher compares
the request method with each candidate method and invalidates non-matches. If
all path candidates fail only on method, endpoint routing returns `405` and an
`Allow` header. There is no special HEAD-to-GET rule in this code path.
Therefore:

- keep the controller-level route
  `TrickplayCropper/Videos/{itemId}/Preview`;
- retain the existing `[HttpGet] GetAsync` action;
- add a distinct `[HttpHead]` action, rather than adding HEAD to the GET action,
  using `AcceptVerbs`, or calling `GetAsync` from HEAD; and
- after both actions exist, unsupported methods expose `Allow: GET, HEAD`.

Sources: ASP.NET Core 9
[`HttpHeadAttribute`](https://github.com/dotnet/aspnetcore/blob/v9.0.11/src/Mvc/Mvc.Core/src/HttpHeadAttribute.cs#L9-L30),
[`HttpMethodMatcherPolicy.ApplyAsync`](https://github.com/dotnet/aspnetcore/blob/v9.0.11/src/Http/Routing/src/Matching/HttpMethodMatcherPolicy.cs#L78-L153),
and its
[`405` endpoint](https://github.com/dotnet/aspnetcore/blob/v9.0.11/src/Http/Routing/src/Matching/HttpMethodMatcherPolicy.cs#L380-L393);
current plugin
[`TrickplayPreviewController`](https://github.com/xiakeng/trickplay-cropper/blob/e7a2c45/src/Jellyfin.Plugin.TrickplayCropper/Api/TrickplayPreviewController.cs#L13-L50).

## Authorization and binding

The controller-level `[Authorize]` metadata applies to both actions. Jellyfin
10.11.11 authenticates before routing, authorizes after routing, and configures
the default policy to use its custom authentication scheme plus
`DefaultAuthorizationRequirement`. Missing or unusable credentials are stopped
before MVC executes the action. A valid API key is accepted by the default
Jellyfin policy and receives an administrator role even without a current user,
so `[Authorize]` alone is not the required content authorization boundary.

The probe must not preserve GET's user-scoped application checks. A valid API key
without a current user may pass the ordinary policy and reach HEAD, while GET
separately rejects it. HEAD resolves the logical Item and Source Video without a
user, enumerates the full playback Media Source set with `user: null`, explicit
media probing disabled, and path substitution disabled, and requires exact identity
and membership. It does not resolve a current user, ask whether the user can see the
Item, or invoke playback authorization. Its success is calculation availability,
not permission evidence. The supported Jellyfin behavior for this enumeration is
recorded in
[the source-enumeration contract](jellyfin-10.11.11-frame-probe-source-enumeration-contract.md).

The current typed binding cannot by itself guarantee an application-level empty
HEAD response. `[ApiController]` turns invalid model state into an automatic
error result before the action, and it also transforms `StatusCodeResult`
client errors into descriptive results. The current `Guid` route parameter,
nullable `Guid` query parameter, and `[BindRequired] long` can all create model
state errors. The HEAD action should therefore accept nullable raw strings,
without `[BindRequired]`, for all three values (for example, `string? itemId`,
`string? mediaSourceId`, and `string? positionTicks`) or use a dedicated binder
that never records required or conversion errors. A non-nullable string is
insufficient unless it has an explicit default, because MVC otherwise infers
`[Required]`. The action must parse all three values inside the action/probe
boundary and return an `EmptyResult` after setting `Response.StatusCode`.
Missing, malformed, or negative `PositionTicks`, and malformed identifiers, map
to an empty `400`. This avoids automatic `ProblemDetails` generation without
changing Jellyfin's global API behavior.

Sources: Jellyfin 10.11.11
[`Startup.Configure`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Server/Startup.cs#L149-L234),
[`AddJellyfinApiAuthorization`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs#L56-L104),
[`CustomAuthenticationHandler`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Api/Auth/CustomAuthenticationHandler.cs#L43-L88),
and
[`DefaultAuthorizationHandler`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs#L41-L93);
ASP.NET Core 9
[`ModelStateInvalidFilter`](https://github.com/dotnet/aspnetcore/blob/v9.0.11/src/Mvc/Mvc.Core/src/Infrastructure/ModelStateInvalidFilter.cs#L71-L80),
[`ClientErrorResultFilter`](https://github.com/dotnet/aspnetcore/blob/v9.0.11/src/Mvc/Mvc.Core/src/Infrastructure/ClientErrorResultFilter.cs#L32-L55),
[`DataAnnotationsMetadataProvider`](https://github.com/dotnet/aspnetcore/blob/v9.0.11/src/Mvc/Mvc.DataAnnotations/src/DataAnnotationsMetadataProvider.cs#L320-L375),
and
[`StatusCodeResult`](https://github.com/dotnet/aspnetcore/blob/v9.0.11/src/Mvc/Mvc.Core/src/StatusCodeResult.cs#L10-L44);
current plugin
[`PreviewQueryParameters`](https://github.com/xiakeng/trickplay-cropper/blob/e7a2c45/src/Jellyfin.Plugin.TrickplayCropper/Api/PreviewQueryParameters.cs#L8-L19)
and
[`JellyfinPreviewSourceResolver`](https://github.com/xiakeng/trickplay-cropper/blob/e7a2c45/src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewSourceResolver.cs#L43-L122).

## HTTP and cache semantics

[RFC 9110 HEAD semantics](https://www.rfc-editor.org/rfc/rfc9110.html#section-9.3.2)
require no response content and recommend the same header fields as GET, while
allowing fields to be omitted when they are determined only while generating
content. Omitting `Content-Length` is correct because a HEAD `Content-Length`,
if sent, must equal the octet count of the corresponding GET representation,
not zero
([RFC 9110 section 8.6](https://www.rfc-editor.org/rfc/rfc9110.html#section-8.6)).
The source-snapshot-derived ETag and generated JPEG length can therefore be
omitted without performing the forbidden work. Omitting the already-known
`Content-Type: image/jpeg` is a deliberate departure from HEAD's `SHOULD`-level
header-parity recommendation, not a violation of its no-content `MUST`.

`private` prevents a shared cache from storing the response, while `no-cache`
allows storage but forbids reuse without successful origin validation
([RFC 9111 `private`](https://www.rfc-editor.org/rfc/rfc9111.html#section-5.2.2.7),
[`no-cache`](https://www.rfc-editor.org/rfc/rfc9111.html#section-5.2.2.4)).
Because the probe exposes no validator, this response is not usefully reusable
by a conforming HTTP cache. That does not constrain the client's separate
application-level image cache.

ASP.NET Core and Jellyfin do not add conditional request handling for this
action. Ignoring `If-None-Match` is mechanically possible and will produce the
same `200` as a request without it. It is, however, a deliberate conditional
HTTP conformance gap: an origin server that receives `If-None-Match` is required
to evaluate it before performing GET or HEAD
([RFC 9110 section 13.1.2](https://www.rfc-editor.org/rfc/rfc9110.html#section-13.1.2)).
The approved no-ETag/no-conditional contract must record that limitation rather
than imply that `If-None-Match` is unsupported by HTTP itself.

## Middleware and body guarantees

Jellyfin's outer middleware can redirect for Base URL or HTTPS, reject by IP,
add response-time and CORS fields, or convert an uncaught exception into a
plain-text error. Consequently, “only two headers” can mean only two
plugin-owned success headers, not the complete wire header set. The probe should
map expected failures itself and avoid throwing after response headers start.

Kestrel normally marks every HEAD response as unable to write content and omits
its automatic zero `Content-Length`. However, ASP.NET Core 9 has a confirmed
Kestrel path where JSON written through `PipeWriter.Advance` before headers can
leak on HEAD; the fix was merged to `main`, not the supported .NET 9 source.
Avoiding automatic model-state and client-error JSON is therefore part of the
contract, not merely an optimization. A real-Kestrel test is needed in addition
to `TestServer`, because transport-level HEAD behavior is outside TestServer.

Sources: Jellyfin 10.11.11
[`ResponseTimeMiddleware`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Api/Middleware/ResponseTimeMiddleware.cs#L41-L66),
[`IpBasedAccessValidationMiddleware`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Api/Middleware/IpBasedAccessValidationMiddleware.cs#L36-L62),
and
[`ExceptionMiddleware`](https://github.com/jellyfin/jellyfin/blob/v10.11.11/Jellyfin.Api/Middleware/ExceptionMiddleware.cs#L51-L100);
ASP.NET Core 9
[`HttpProtocol`](https://github.com/dotnet/aspnetcore/blob/v9.0.11/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs#L1163-L1206),
[`CanWriteResponseBody`](https://github.com/dotnet/aspnetcore/blob/v9.0.11/src/Servers/Kestrel/Core/src/Internal/Http/HttpProtocol.cs#L1285-L1301),
the official
[.NET 9 HEAD body bug](https://github.com/dotnet/aspnetcore/issues/59691),
and its
[main-branch fix](https://github.com/dotnet/aspnetcore/pull/59725).

## Enforceable no-GET boundary

The existing resolver cannot be reused unchanged: after its authorization and
metadata work, it calls `GetTrickplayTilePathAsync`, checks the file, and reads
length and modification time. The GET module then creates Preview Identity,
checks ETag, enters the Preview Cache, and can call the encoder. The implementation
seam must instead be:

1. A dedicated `ITrickplayFrameProbe` dependency on the HEAD action.
2. Separate GET user authorization and HEAD user-independent source adapters.
3. Shared request-local target copying, metadata validation, and Frame Index
   calculation after each adapter establishes its required source facts.
4. The shared calculation derives
   `min(PositionTicks / (Interval * TimeSpan.TicksPerMillisecond), ThumbnailCount - 1)`
   with checked arithmetic and validates exact selected metadata consistently for
   both operations.
5. A GET-only continuation through `FrameSelection.Create` for Source Sprite,
   cell, and crop geometry, followed by tile path, file snapshot, Preview
   Identity, conditional ETag, Preview Cache, decoder, and encoder work.

Tests should make this boundary executable: dependency-direction tests keep
current-user, user-scoped context, Source Sprite, cache, and encoder facilities out
of the probe path; the Cache Tree must remain unchanged; and real-Kestrel HTTP tests
must assert an empty content stream for success, malformed input, authorization
failure, and mapped domain failures. The success test must also assert invariant
decimal Frame Index formatting, the two plugin headers, and absence of ETag and
all GET-only headers. Source-adapter tests must cover the supported default, local
alternate, linked, and eligible dynamic source shapes and document that the fake
does not execute Jellyfin provider or filesystem behavior.

Sources: current plugin
[`JellyfinPreviewSourceResolver`](https://github.com/xiakeng/trickplay-cropper/blob/e7a2c45/src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewSourceResolver.cs#L125-L193),
[`TrickplayPreview.GetResolvedAsync`](https://github.com/xiakeng/trickplay-cropper/blob/e7a2c45/src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayPreview.cs#L99-L121),
and
[`FrameSelection.Create`](https://github.com/xiakeng/trickplay-cropper/blob/e7a2c45/src/Jellyfin.Plugin.TrickplayCropper/Preview/FrameSelection.cs#L30-L76).
