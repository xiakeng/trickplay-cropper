# Jellyfin 10.11.11 Administration API Contract for Local Integration

Research date: 2026-09-02. Sources in this note are pinned to the Jellyfin
`v10.11.11` tag (commit `1fbd8739292cce610231be93daf43368733edf63`) unless a
repository source is explicitly identified. The harness described here is a
manual, local-only test tool. It must fail closed when the server cannot supply
the required video, alternate-source, or Trickplay evidence; it must not use
mocks or infer server state from HTTP success alone.

## Authentication and credential identity

Jellyfin's custom authentication handler calls `IAuthService.Authenticate` and
creates claims for the token, user id, role, and `Jellyfin-IsApiKey`. A valid
user access token resolves a device record and its user. A server API key is
looked up in the `ApiKeys` table when no device token matches, has no user, and
sets `IsApiKey = true`. Both are accepted by the default policy, but API keys
are treated as unrestricted administrator credentials by the default
authorization handler. The harness must therefore keep these credential types
separate and must never use an API key as evidence of user-scoped playback
visibility. [Custom authentication handler][custom-auth] [Authorization
context][authorization-context] [Default authorization policy][default-auth]

The supported request forms are:

```http
Authorization: MediaBrowser Client="TrickplayCropperHarness", Device="local", DeviceId="...", Version="1.0", Token="<user-access-token>"
```

When legacy authorization is enabled, Jellyfin also accepts
`X-Emby-Authorization`, `X-Emby-Token`, and `X-MediaBrowser-Token`. `ApiKey` is
accepted as a query parameter, and `api_key` is accepted only with legacy
authorization enabled. The canonical non-legacy form for a harness is the
`Authorization: MediaBrowser ... Token=...` header; query-string credentials
must not be written to logs or persisted. [Authorization context parsing][authorization-context]

The harness may obtain a disposable user access token without out-of-band
state by posting credentials to `POST /Users/AuthenticateByName` with an
`AuthenticateUserByName` JSON body. The response is an
`AuthenticationResult` containing `User`, `SessionInfo`, `AccessToken`, and
`ServerId`; keep the returned token in process memory only and revoke/discard
the session after the run. A pre-provisioned token is preferable for a repeatable
manual run. [User authentication endpoint][user-auth] [Authentication result][auth-result]

The user token can be verified without a mock by calling `GET /Users/{userId}`
with the token and checking that the returned user id matches the token's
intended account. `GET /Users` is also available to an authenticated caller,
while `GET /Users/Public` is unauthenticated and is not proof of authorization.
The user controller documents these routes and returns `404` for an unknown
user. [User controller][user-controller]

`GET /Auth/Keys` is an administrator-only inventory of configured API keys; key
creation and revocation are `POST /Auth/Keys?app=...` and
`DELETE /Auth/Keys/{key}`. The endpoint does not return a user association, so
the harness should identify an API-key run by using a deliberately provisioned
test key and observing the server's behavior, never by guessing token shape.
The server-side distinction is the `IsApiKey` claim described above.
[API-key controller][api-key-controller]

## Enumerating visible videos

Use the authenticated `GET /Items` endpoint with a user id and a video filter:

```http
GET /Items?userId=<user-id>&includeItemTypes=Movie,Episode&mediaTypes=Video&recursive=true&startIndex=0&limit=100&enableTotalRecordCount=true&fields=Path,MediaStreams
```

`ItemsController.GetItems` accepts `userId`, `includeItemTypes`, `mediaTypes`,
`recursive`, paging, and fields. It resolves the user, obtains the user's
parent folder, rejects an inaccessible folder, and builds an
`InternalItemsQuery(user)`; the result is a `QueryResult<BaseItemDto>` with
`Items` and `TotalRecordCount`. Use the returned DTO `Id`, `Type`, `MediaType`,
`IsFolder`, and `Path` only as discovery data. For a local integration run,
re-query each candidate through the playback endpoint below and require a
non-empty local Media Source before selecting it. [Items controller][items-controller]

The same endpoint is intentionally user-scoped. An API key may omit a user and
is allowed to see all folders by the default policy, but that result cannot
stand in for a user's visible-library assertion. A harness must run discovery
with a real user access token and record the user id used.

## Alternate Media Sources and source-specific identity

For each discovered logical video, call:

```http
GET /Items/{item-id}/PlaybackInfo?userId=<user-id>
```

Jellyfin resolves the item with the user and returns `PlaybackInfoResponse`,
whose `MediaSources` entries carry source ids. The POST variant can accept a
`PlaybackInfoDto`, but GET is sufficient for enumeration and avoids opening a
stream. [Media info controller][media-info-controller]

Treat every returned `MediaSources[].Id` as an independent source identity.
For an alternate version, the id is normally a GUID for the alternate Source
Video. Jellyfin's Trickplay manager generates and reads metadata by that source
video id; its manifest construction parses each local GUID-valued source id and
queries `GetTrickplayResolutions` independently, skipping remote or non-GUID
sources. The harness must therefore:

1. retain the logical item id and selected `MediaSource.Id` separately;
2. require the selected source id to be a GUID and a member of the playback
   response;
3. call `GET /Items/{source-id}/PlaybackInfo?userId=...` (or the equivalent
   user-scoped item lookup) to confirm the Source Video is visible and playable;
4. query Trickplay evidence using the Source Video id, never the logical id and
   never a fallback source.

The source-specific rule is implemented by the pinned `ITrickplayManager`
contract and `TrickplayManager.GetTrickplayManifest`; it is not a client-side
path convention. [Trickplay manager interface][trickplay-interface] [Manifest
construction][trickplay-manifest] [Repository resolution contract][resolution-research]

## Trickplay coverage evidence

The public HTTP Trickplay routes can prove that a tile is retrievable:

```http
GET /Videos/{logical-item-id}/Trickplay/{width}/tiles.m3u8?MediaSourceId=<source-id>
GET /Videos/{logical-item-id}/Trickplay/{width}/{index}.jpg?MediaSourceId=<source-id>
```

The controller passes `mediaSourceId ?? itemId` to the manager for the HLS
playlist and resolves the tile item by the selected source id. A non-empty
playlist and a `200` JPEG prove only that the requested width/index is
currently available; they do not prove every generated frame or the plugin's
selected resolution. [Jellyfin Trickplay controller][trickplay-controller]

For complete coverage evidence, use the metadata exposed in item DTOs when the
`Trickplay` field is requested (`fields=Trickplay`), or inspect the manager's
source contract in a host-side diagnostic. `TrickplayInfoDto` contains
`TileWidth`, `TileHeight`, `ThumbnailCount`, and `Interval`; `BaseItemDto.Trickplay`
is keyed by source id and resolution. Require a positive `ThumbnailCount` and
positive geometry/interval, then probe at least index `0` and the last index
(`ThumbnailCount - 1`) through the tile endpoint. [Base item DTO][base-item-dto]
[Trickplay info DTO][trickplay-info-dto]

The repository's selected-resolution policy additionally requires reading the
server `TrickplayOptions.WidthResolutions`, mirroring Jellyfin's even-width
normalization for the selected Source Video, and finding one exact generated
metadata key. Current configuration is not proof of generated data: Jellyfin
stores rows per `(SourceVideoId, Width)` and can retain stale rows. Use the
existing resolution research as the governing product policy. [Server
configuration][server-configuration] [Trickplay resolution research][resolution-research]

## Inspecting plugin and server state

The administrator-only `GET /Plugins` route returns installed `PluginInfo`
records ordered by name. Use it to confirm the Trickplay Cropper plugin id,
version, and status after deployment. `GET /Plugins/{pluginId}/Configuration`
returns plugin configuration when the plugin implements
`IHasPluginConfiguration`; the corresponding POST mutates configuration and
must be avoided unless the scenario explicitly requires it. [Plugins controller][plugins-controller]

`GET /System/Info` returns `SystemInfo`, including the running server version;
the harness must assert `10.11.11` before collecting results. `GET
/System/Configuration` returns the full `ServerConfiguration`, including
`TrickplayOptions`; it is authenticated, while the POST replacement endpoint
requires elevation and replaces the entire object. Snapshot the JSON before any
intentional mutation and restore the exact snapshot. [System controller][system-controller]
[Configuration controller][configuration-controller]

## Triggering and awaiting the cleanup scheduled task

The plugin registers `ClearTrickplayCropperCacheTask` with key
`ClearTrickplayCropperCache`; its default trigger is daily at 03:00 and its
`ExecuteAsync` delegates to `IPreviewCacheMaintenance.ClearAsync`. The task is
therefore the host-owned control point for cache cleanup, not a direct call to
the plugin service. [Repository cleanup task][cleanup-task]

Use the administrator-only scheduled-task API:

```http
GET  /ScheduledTasks?isHidden=false
GET  /ScheduledTasks/{task-id}
POST /ScheduledTasks/Running/{task-id}
```

Find the task whose `Key` is `ClearTrickplayCropperCache`, capture its
`LastExecutionResult` and `State`, then POST `Running/{task-id}`. The POST
returns `204` after enqueueing execution; it does not wait. Poll
`GET /ScheduledTasks/{task-id}` until `State` returns to `Idle` and
`LastExecutionResult.EndTimeUtc` is later than the captured start, then require
`LastExecutionResult.Status` to be successful and inspect the plugin cleanup
summary in the logs. `TaskInfo` exposes `State`, `CurrentProgressPercentage`,
`LastExecutionResult`, `Id`, and `Key`; the state enum is `Idle`, `Cancelling`, or
`Running`. [Scheduled tasks controller][scheduled-tasks-controller] [TaskInfo][task-info]
[Task state][task-state]

Do not use `DELETE /ScheduledTasks/Running/{task-id}` in the normal harness; it
cancels a running task and would invalidate cleanup evidence. A timeout is a
failed integration result, followed by restoration and service-health checks.

## Reading logs and changing debug logging

Use `GET /System/Logs` (elevated) to list log files with name, size, and UTC
timestamps, then `GET /System/Logs/Log?name=<exact-name>` to stream one file as
`text/plain`. The server validates the requested name against its configured
log directory. Capture the newest file name before a scenario and read only
that file after the task or probe. [System log endpoints][system-controller]

Jellyfin 10.11.11 has no administration HTTP endpoint for changing Serilog
category levels. At startup it loads `<ConfigurationDirectoryPath>/logging.json`
with reload-on-change, after creating a default logging file when absent; the
default minimum level is `Information`. Enabling debug logging is therefore a
local-host contract: snapshot `logging.json`, make the smallest JSON change
(normally `MinimumLevel.Default` to `Debug` or a scoped override), restart the
systemd Jellyfin service, and verify a fresh log contains the expected debug
event. Restore the byte-for-byte snapshot and restart again, then verify the
effective level is back to its original value. Never put tokens or API keys in
the edited file or in captured logs. [Logging initialization][logging-init]
[Default logging resource][logging-resource]

The harness must also snapshot and restore any service state it changes. On a
native systemd installation that means recording `systemctl is-active` and
`systemctl is-enabled` before a restart, using the configured unit (commonly
`jellyfin.service`), and returning it to the recorded state. This is a host
privilege contract, not a Jellyfin HTTP API.

## Manual integration sequence and fail-closed rules

1. Assert server version `10.11.11` from `/System/Info`; authenticate with a
   user access token and record its user id. Optionally run a separate API-key
   smoke check, clearly labeled as administrator/unscoped.
2. Enumerate `/Items` with `includeItemTypes=Movie,Episode`, `mediaTypes=Video`,
   `recursive=true`, and paging. Require the configured minimum number of
   distinct suitable videos; otherwise stop with an explicit coverage failure.
3. For each candidate, call `/Items/{id}/PlaybackInfo`, retain all source ids,
   and select at least one logical source plus one alternate Source Video when
   the library provides one. Re-check source visibility with the user token.
4. Read `/System/Configuration` and item `Trickplay` metadata, apply the
   repository's exact Selected Trickplay Resolution policy, and probe playlist
   and boundary JPEGs. Missing metadata, non-GUID alternate ids, or inaccessible
   sources are failures, not reasons to silently substitute another item.
5. Confirm `/Plugins` contains the expected plugin. Snapshot the plugin Cache
   Tree and newest log file, trigger `ClearTrickplayCropperCache`, poll task state
   to completion, and verify cleanup logs plus the expected Cache Tree change.
6. If debug evidence is required, snapshot `logging.json`, apply the minimal
   local edit, restart, collect evidence, restore the snapshot, restart, and
   verify service and logging state. A failed restore or health check fails the
   run and must be reported separately from the original assertion.

## Sources

[custom-auth]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Auth/CustomAuthenticationHandler.cs#L43-L88
[authorization-context]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs#L43-L220
[default-auth]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs#L41-L93
[user-controller]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/UserController.cs#L84-L142
[user-auth]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/UserController.cs#L203-L235
[auth-result]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Authentication/AuthenticationResult.cs#L7-L30
[api-key-controller]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/ApiKeyController.cs#L30-L74
[items-controller]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/ItemsController.cs#L160-L162
[media-info-controller]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/MediaInfoController.cs#L64-L88
[trickplay-interface]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Trickplay/ITrickplayManager.cs#L39-L95
[trickplay-manifest]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L562-L581
[trickplay-controller]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/TrickplayController.cs#L42-L103
[base-item-dto]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Dto/BaseItemDto.cs#L560-L578
[trickplay-info-dto]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Dto/TrickplayInfoDto.cs#L9-L56
[server-configuration]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Configuration/ServerConfiguration.cs#L279-L287
[plugins-controller]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/PluginsController.cs#L46-L57
[system-controller]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/SystemController.cs#L64-L76
[configuration-controller]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/ConfigurationController.cs#L45-L69
[cleanup-task]: https://github.com/xiakeng/trickplay-cropper/blob/dcb5b613c34b5aeb9c7f99bb36f115b3bada65c7/src/Jellyfin.Plugin.TrickplayCropper/Tasks/ClearTrickplayCropperCacheTask.cs#L9-L55
[scheduled-tasks-controller]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/ScheduledTasksController.cs#L31-L110
[task-info]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Tasks/TaskInfo.cs#L20-L78
[task-state]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Tasks/TaskState.cs#L1-L22
[logging-init]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server/Helpers/StartupHelpers.cs#L220-L288
[logging-resource]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server/Resources/Configuration/logging.json#L1-L18
[resolution-research]: ./jellyfin-10.11.11-trickplay-resolution-contract.md
