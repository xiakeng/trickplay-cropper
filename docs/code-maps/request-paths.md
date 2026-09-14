# Request paths map

`TrickplayCropper/Videos/{itemId}/Preview` routes direct Frame Index GET requests.
`TrickplayCropper/Videos/{itemId}/FrameTimeline` returns the authorized generated
interval and frame count before image work.

## Controller

HTTP routing lives in
[TrickplayPreviewController.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Api/TrickplayPreviewController.cs):
`GetAsync` binds `PreviewQueryParameters` and conditional ETags; `GetFrameTimelineAsync`
binds the optional `MediaSourceId`. `MapOutcome` and `MapFrameTimelineOutcome` map
closed outcomes to HTTP responses. Both routes use Jellyfin's default authenticated
policy; the resolver additionally requires a non-empty `Jellyfin-UserId` (or returns
forbidden for an API key without a current user).

## GET chain

`GetAsync` → `TrickplayPreview.GetAsync` → `JellyfinPreviewContextResolver.ResolveAsync`
(user authority, concealment) → `ResolveForPreviewAsync` (resolution, metadata, and
validated Frame Index) → `JellyfinPreviewSourceResolver.ResolveAsync` (Source Sprite
snapshot) → `PreviewIdentity.Create` → conditional `If-None-Match` check →
`DiskPreviewCache.GetOrCreateAsync` → `TrickplayPreviewEncoder.EncodeAsync` → outcome.

| Target | Key symbols | Responsibility |
|---|---|---|
| [TrickplayPreview.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayPreview.cs) | `GetAsync`, `ProcessAsync`, `PreviewFailureLog` | GET orchestration; unexpected failures become `InternalError` with one redacted Error log |
| [JellyfinPreviewContextResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewContextResolver.cs) | `ResolveAsync`, `ResolveAuthorizedSourceAsync`, `JellyfinUserIdClaim`, `JellyfinIsApiKeyClaim` | User-scoped authorization, concealment, and selected source resolution |
| [JellyfinTrickplayFrameCalculationResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinTrickplayFrameCalculationResolver.cs) | `ResolveForPreviewAsync`, `ResolveForTimelineAsync` | Resolution selection, metadata validation, and Frame Index calculation |
| [TrickplayMetadataReader.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/TrickplayMetadataReader.cs) | `GetForPreviewAsync`, `ReadAuthoritativeTimelineAsync` | Current generated-metadata reads for each request |
| [JellyfinPreviewSourceResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewSourceResolver.cs) | `ResolveAsync` | Source Sprite path and version facts |
| [TrickplayPreviewEncoder.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Imaging/TrickplayPreviewEncoder.cs) | `EncodeAsync` | JPEG crop and encode of the selected sprite cell |

## Frame Timeline chain

`GetFrameTimelineAsync` → `TrickplayFrameTimeline.GetAsync` →
`JellyfinPreviewContextResolver.ResolveAuthorizedSourceAsync` →
`ResolveForTimelineAsync` → `FrameTimelineOutcome.Success(IntervalTicks, FrameCount)`
→ JSON response (`intervalTicks`, `frameCount`).

| Target | Key symbols | Responsibility |
|---|---|---|
| [TrickplayFrameTimeline.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayFrameTimeline.cs) | `GetAsync` | Timeline orchestration; no sprite, cache, conditional, or encoder access |
| [FrameTimeline.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Preview/FrameTimeline.cs) | `IFrameTimeline`, `FrameTimelineOutcome` | Timeline service boundary and closed HTTP-facing outcomes |

## Test entry points

Suite locations are on the [tests map](tests.md).

| Behavior | Entry point |
|---|---|
| GET responses, authorization, and failures | `TrickplayPreviewGetResponseHttpSpecs.cs`, `TrickplayPreviewAuthorizationHttpSpecs.cs`, `TrickplayPreviewFailureHttpSpecs.cs` (ComponentTests) |
| Frame Timeline HTTP contract, source binding, and no-image work | `TrickplayFrameTimelineHttpSpecs.cs` (ComponentTests) |
| Real Kestrel wiring | `TrickplayPreviewKestrelHttpSpecs.cs` (ComponentTests) |
| GET outcome mapping and conditional requests | `PreviewOutcomeSpecs.cs` (UnitTests) |
