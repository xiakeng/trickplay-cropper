# Authenticated item and media-source access flow

## Scope and target

This note resolves the access-control sequence for the preview endpoint against
Jellyfin **10.11.11**, the current stable release when this research was
performed on 2026-09-01. The release is identified by the official
[`v10.11.11` release](https://github.com/jellyfin/jellyfin/releases/tag/v10.11.11)
and source commit
[`1fbd8739292cce610231be93daf43368733edf63`](https://github.com/jellyfin/jellyfin/tree/1fbd8739292cce610231be93daf43368733edf63).

The decision below applies to a user-bound preview endpoint. It deliberately
does not grant server API keys an implicit user identity.

## Decision

The controller must use Jellyfin's normal `[Authorize]` attribute, resolve the
authenticated user from Jellyfin's claims, perform user-scoped lookups for both
the logical video and the selected source video, verify general playback
permission, and prove that the selected source ID occurs in the logical video's
playback media-source enumeration. Every one of these checks must finish before
looking at a shared preview-cache file, evaluating its ETag, or returning `304`.

Missing and user-invisible resources must be indistinguishable (`404`). `403`
is reserved for an authenticated principal that is rejected by Jellyfin's
default authorization policy, a server API key that has no current-user
identity, or a visible item whose user has media playback disabled. `401` is
reserved for absent, invalid, disabled, or otherwise unusable user-session
authentication.

## Primary-source findings

### Authentication and current-user identity

- Jellyfin's own trickplay and playback-info controllers put `[Authorize]` on
  the controller, so an unqualified attribute uses Jellyfin's configured
  default policy. The default policy selects the custom Jellyfin authentication
  scheme and its `DefaultAuthorizationRequirement` ([policy registration](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs#L56-L71),
  [official trickplay controller](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/TrickplayController.cs#L22-L24),
  [official playback-info controller](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/MediaInfoController.cs#L28-L30)).
- The authentication handler places the authenticated user's ID and whether the
  credential is a server API key into claims. `User.GetUserId()` and
  `User.GetIsApiKey()` are the public claim accessors
  ([claim creation](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Auth/CustomAuthenticationHandler.cs#L42-L77),
  [claim accessors](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Extensions/ClaimsPrincipalExtensions.cs#L13-L75)).
  Resolve the `User` with `IUserManager.GetUserById(userId)`; the interface
  explicitly returns `null` when the user does not exist
  ([`IUserManager`](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Library/IUserManager.cs#L42-L48)).
- Missing tokens produce no authentication result; invalid and disabled-user
  tokens fail authentication before authorization completes
  ([token validation](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Emby.Server.Implementations/HttpServer/Security/AuthService.cs#L21-L40),
  [authentication result mapping](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Auth/CustomAuthenticationHandler.cs#L42-L87)).
- A server API key is authenticated without a `User`; `AuthorizationInfo.UserId`
  is consequently `Guid.Empty`. Jellyfin's default authorization handler treats
  API keys as unrestricted, so `[Authorize]` alone does **not** establish a
  current user
  ([API-key resolution](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs#L186-L217),
  [`AuthorizationInfo.UserId`](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Net/AuthorizationInfo.cs#L12-L17),
  [API keys bypass the default user checks](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs#L41-L63)).
  The preview action must therefore return `Forbid()` when
  `User.GetIsApiKey()` is true instead of accidentally performing an unscoped
  library lookup with an empty user ID.
- The default policy also enforces remote-access permission and the user's
  parental schedule before the action runs
  ([default policy checks](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs#L58-L91)).
- The supported non-legacy authorization schema is `Authorization:
  MediaBrowser ...`. `X-Emby-Token` is accepted only while
  `EnableLegacyAuthorization` is enabled; it defaults to `true` in 10.11.11
  ([token extraction](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs#L88-L125),
  [authorization schema parsing](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs#L228-L269),
  [configuration default](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Configuration/ServerConfiguration.cs#L287-L290)).
  The plugin must not parse any of these forms itself. Its contract should say
  that it uses Jellyfin's configured authentication; Kodi's existing
  `X-Emby-Token` works under the stable default but is not an unconditional
  plugin guarantee.

### User-visible item lookup and playback permission

- Use `ILibraryManager.GetItemById<Video>(id, user)` for both IDs. The overload
  is documented as validating user access
  ([interface contract](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Library/ILibraryManager.cs#L188-L206)).
  Its implementation returns `null` for both an absent item and an item that is
  not visible to the user
  ([implementation](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Emby.Server.Implementations/Library/LibraryManager.cs#L1392-L1406),
  [visibility gate](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Emby.Server.Implementations/Library/LibraryManager.cs#L3355-L3368)).
  Visibility includes item/ancestor parental and tag rules plus membership in a
  user-visible library folder
  ([standalone visibility](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Entities/BaseItem.cs#L1337-L1367),
  [parental and tag visibility](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Entities/BaseItem.cs#L1584-L1641)).
  This deliberate collapse is why missing and inaccessible items both map to
  `404`; a raw `GetItemById(id)` probe must not be used to choose between `403`
  and `404`.
- Visibility does not check the user's general media-playback permission.
  `BaseItem.GetPlayAccess(user)` separately maps
  `PermissionKind.EnableMediaPlayback` to `PlayAccess.Full` or
  `PlayAccess.None`
  ([play-access implementation](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Entities/BaseItem.cs#L1050-L1068)).
  A preview is derived from playable media, so require `PlayAccess.Full` and
  return `Forbid()` otherwise. Transcoding, remuxing, downloading, and
  administrator permissions are not additional requirements for reading an
  already-generated trickplay image.

### Playback media sources and alternate-version membership

- Enumerate with
  `IMediaSourceManager.GetPlaybackMediaSources(logicalVideo, user,
  allowMediaProbe: false, enablePathSubstitution: false,
  HttpContext.RequestAborted)`. This is the same server abstraction used by the
  playback-info path
  ([interface](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Library/IMediaSourceManager.cs#L55-L64),
  [playback-info use](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Helpers/MediaInfoHelper.cs#L87-L110)).
  For this membership-only call, this research recommends `false` for both
  flags: the endpoint neither needs to refresh/probe metadata nor consumes a
  substituted path.
- For videos, the static source set consists of the video itself, linked
  alternates, its primary and sibling alternates when invoked on an alternate,
  and local alternate files
  ([video source set](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Entities/Video.cs#L533-L563)).
  Jellyfin assigns each static `MediaSourceInfo.Id` from the corresponding
  item's GUID using the `N` format
  ([media-source construction](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Entities/BaseItem.cs#L1125-L1155)).
- Normalize `resolvedMediaSourceId` as lowercase `N` and require an
  ordinal-ignore-case match against one enumerated `MediaSourceInfo.Id`. Also
  require the selected GUID to resolve separately through
  `GetItemById<Video>(resolvedMediaSourceId, user)`. Enumeration proves group
  membership; the second user-scoped lookup proves that a linked version is
  itself visible. Both checks are necessary because the video source-set code
  loads linked items without a user argument.
- A dynamic source whose string ID happens to parse as a GUID but has no
  user-visible library `Video` is outside this v1 flow and returns `404`.
  Trickplay metadata is keyed by a library media item/source GUID, so accepting
  such a source would not lead to the required trickplay lookup
  ([`ITrickplayManager.GetTrickplayResolutions`](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Trickplay/ITrickplayManager.cs#L39-L44)).

## Required action sequence

The access boundary should be implemented in this order:

1. Let MVC bind and validate the route/query values; reject a negative
   `PositionTicks` with `400`.
2. Let `[Authorize]` run Jellyfin's default authentication and authorization
   policy.
3. Reject a server API key with `Forbid()`. Read `User.GetUserId()`; return
   `Unauthorized()` if it is empty or no longer resolves to a `User`.
4. Resolve `ItemId` with `GetItemById<Video>(itemId, user)`; return `NotFound()`
   if it returns `null`.
5. Require `logicalVideo.GetPlayAccess(user) == PlayAccess.Full`; otherwise
   return `Forbid()`.
6. Set `resolvedMediaSourceId = MediaSourceId ?? ItemId`.
7. Resolve `resolvedMediaSourceId` with
   `GetItemById<Video>(resolvedMediaSourceId, user)`; return `NotFound()` if it
   returns `null`, and require `PlayAccess.Full` for that selected item.
8. Enumerate the logical video's playback media sources and require an ID match
   for `resolvedMediaSourceId`; return `NotFound()` if there is none.
9. Only now query trickplay metadata/path, inspect a shared cached preview,
   compare `If-None-Match`, or return a cached file/`304`.

The two item lookups and the membership check are intentionally ordered before
the cache. A cache hit is not an authorization result, and a user ID must not be
added to the cache key as a substitute for revalidation.

## HTTP status mapping

| Status | Conditions in this flow |
| --- | --- |
| `401 Unauthorized` | Missing/invalid/disabled user-session token handled by Jellyfin authentication; defensive empty or stale non-API-key user identity in the action. |
| `403 Forbidden` | Jellyfin default policy rejects the authenticated user (for example remote access or parental schedule); authenticated server API key has no current-user identity; visible logical/selected video has `PlayAccess.None`. |
| `404 Not Found` | Logical or selected video is missing, is not a `Video`, or is not visible to the current user; requested media-source GUID is not a member of the logical video's enumerated playback sources. Later trickplay metadata/sprite absence also remains `404`. |

ASP.NET Core's controller results give the intended codes directly:
`Unauthorized()` produces 401 and `Forbid()` produces 403
([Microsoft `Unauthorized` documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.controllerbase.unauthorized?view=aspnetcore-10.0),
[Microsoft `Forbid` documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.controllerbase.forbid?view=aspnetcore-10.0)).
Jellyfin's exception middleware independently maps authentication, security,
and not-found exceptions to 401, 403, and 404 respectively
([exception mapping](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Middleware/ExceptionMiddleware.cs#L123-L135)).

## Contract consequence

The original requirement's broad statement that "no access to item/media
source" returns `403` should be narrowed. Returning `403` after a raw lookup
would reveal that an otherwise hidden item exists. The safe contract is:

- `403` for policy/playback denial when the principal or visible item is already
  established; and
- `404` for absent, hidden, wrong-type, or non-member item/source IDs.
