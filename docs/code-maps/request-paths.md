# Request paths map

`TrickplayCropper/Videos/{itemId}/Preview` serves direct Frame Index GETs, while
`TrickplayCropper/Videos/{itemId}/FrameTimeline` returns the current playback timeline.
Both use the same current-user source authorization and authoritative metadata path.

## Controller

HTTP routing lives in
[TrickplayPreviewController.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Api/TrickplayPreviewController.cs):
`GetAsync` binds required `FrameIndex` and optional `MediaSourceId`; `GetFrameTimelineAsync`
binds the logical Item and optional source. The controller maps closed Preview and Frame
Timeline outcomes, preserves Jellyfin authentication, and emits the direct-index JPEG or
the exact `intervalTicks`/`frameCount` JSON response.

## Preview GET chain

`GetAsync` → `TrickplayPreview.GetAsync` → `JellyfinPreviewContextResolver.ResolveAsync`
(user authority, concealment, current metadata and Frame Index validation) →
`JellyfinPreviewSourceResolver.ResolveAsync` (Source Sprite snapshot) →
`PreviewIdentity.Create` → conditional `If-None-Match` check →
`DiskPreviewCache.GetOrCreateAsync` → `TrickplayPreviewEncoder.EncodeAsync` → outcome.

| Target | Key symbols | Responsibility |
|---|---|---|
| [TrickplayPreview.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayPreview.cs) | `GetAsync`, `ProcessAsync` | GET orchestration; unexpected failures become `InternalError` with one redacted Error log |
| [JellyfinPreviewContextResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewContextResolver.cs) | `ResolveAsync`, `ResolveAuthorizedSourceAsync`, `JellyfinUserIdClaim`, `JellyfinIsApiKeyClaim` | Full current-user authorization, concealment, selected source, and Preview calculation handoff |
| [JellyfinTrickplayFrameCalculationResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinTrickplayFrameCalculationResolver.cs) | `ResolveForPreviewAsync`, `ResolveForTimelineAsync` | Resolution selection and authoritative metadata calculation for both endpoints |
| [TrickplayMetadataReader.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/TrickplayMetadataReader.cs) | `GetForPreviewAsync`, `ReadAuthoritativeTimelineAsync` | Per-request generated metadata reads and validation |
| [JellyfinPreviewSourceResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewSourceResolver.cs) | `ResolveAsync` | Source Sprite path and version facts |
| [TrickplayPreviewEncoder.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Imaging/TrickplayPreviewEncoder.cs) | `EncodeAsync` | JPEG crop and encode of the selected sprite cell |

## Frame Timeline chain

`GetFrameTimelineAsync` → `TrickplayFrameTimeline.GetAsync` →
`JellyfinPreviewContextResolver.ResolveAuthorizedSourceAsync` →
`ResolveForTimelineAsync` → `TrickplayMetadataReader.ReadAuthoritativeTimelineAsync` →
`FrameTimelineOutcome.Success(intervalTicks, frameCount)` → JSON response.

| Target | Key symbols | Responsibility |
|---|---|---|
| [TrickplayFrameTimeline.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayFrameTimeline.cs) | `GetAsync` | Authorized timeline orchestration without sprite, cache, or encoder access |
| [AuthorizedSourceResolution.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/AuthorizedSourceResolution.cs) | `Resolved`, `Unauthorized`, `Forbidden`, `NotFound` | Closed current-user source authorization results |

## Test entry points

Suite locations are on the [tests map](tests.md).

| Behavior | Entry point |
|---|---|
| Preview GET responses, authorization, failures, and conditional requests | `TrickplayPreviewGetResponseHttpSpecs.cs`, `TrickplayPreviewAuthorizationHttpSpecs.cs`, `TrickplayPreviewFailureHttpSpecs.cs` (ComponentTests) |
| Frame Timeline HTTP contract and source binding | `TrickplayFrameTimelineHttpSpecs.cs` (ComponentTests) |
| Real Kestrel wiring | `TrickplayPreviewKestrelHttpSpecs.cs` (ComponentTests) |
| Preview outcome mapping and identity | `PreviewOutcomeSpecs.cs`, `PreviewIdentitySpecs.cs` (UnitTests) |
