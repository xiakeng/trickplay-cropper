# Request paths map

One controller routes both operations at `TrickplayCropper/Videos/{itemId}/Preview`.
After binding, the paths diverge: GET resolves a user-authorized context and serves a
representation; the Trickplay Frame Probe answers *which frame this position selects*
and stops before any image work. Both paths share `PreviewQuery` and the observation
caches on the [caching map](caching.md).

## Controller

The only HTTP surface is
[TrickplayPreviewController.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Api/TrickplayPreviewController.cs):
`GetAsync` binds the query, `HeadAsync` binds raw strings and rejects malformed input
(`TryCreateQuery`), and `MapOutcome` with `CreateBodylessResult` map outcomes to wire
responses and headers. Closed outcome sets: `PreviewOutcome` (GET) and
`TrickplayFrameProbeOutcome` (HEAD, no conditional variant); the
[response contract](../business/lifecycle/response-contract.md) owns statuses and
headers.

## GET chain

`GetAsync` → `TrickplayPreview.ProcessAsync` → `JellyfinPreviewContextResolver.ResolveAsync`
(user authority, concealment) → `ResolveForPreviewAsync` (Selected Trickplay
Resolution and Frame Index) → `JellyfinPreviewSourceResolver.ResolveAsync` (Source Sprite
snapshot) → `PreviewIdentity.Create` → conditional `If-None-Match` check →
`DiskPreviewCache.GetOrCreateAsync` → `TrickplayPreviewEncoder.EncodeAsync` → outcome.

| Target | Key symbols | Responsibility |
|---|---|---|
| [TrickplayPreview.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayPreview.cs) | `ProcessAsync`, `PreviewFailureLog` | GET orchestration; unexpected failures become `InternalError` with one redacted Error log |
| [JellyfinPreviewContextResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewContextResolver.cs) | `ResolveAsync`, `JellyfinUserIdClaim`, `JellyfinIsApiKeyClaim` | User-scoped authorization with concealment; publishes only user-verified source facts |
| [JellyfinTrickplayFrameCalculationResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinTrickplayFrameCalculationResolver.cs) | `ResolveForPreviewAsync`, `ResolveForProbeAsync` | Resolution selection and Frame Index calculation shared by both paths |
| [JellyfinPreviewSourceResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewSourceResolver.cs) | `ResolveAsync` | Source Sprite path and version facts |
| [TrickplayPreviewEncoder.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Imaging/TrickplayPreviewEncoder.cs) | `EncodeAsync` | JPEG crop and encode of the selected sprite cell |

## Trickplay Frame Probe chain

`HeadAsync` → `TrickplayFrameProbe.ProbeAsync` (rejects negative positions) →
`JellyfinTrickplayFrameProbeContextResolver.ResolveAsync` →
`TrickplaySourceFactsCache.GetForProbeAsync` → `ResolveForProbeAsync` →
`TrickplayFrameProbeOutcome.Success(FrameIndex)` → bodyless response carrying
`X-Trickplay-Frame-Index`.

| Target | Key symbols | Responsibility |
|---|---|---|
| [TrickplayFrameProbe.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayFrameProbe.cs) | `ProbeAsync` | Probe module; no user, sprite, cache, or encoder access |
| [JellyfinTrickplayFrameProbeContextResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinTrickplayFrameProbeContextResolver.cs) | `ResolveAsync` | User-independent source-facts and calculation path |

## Test entry points

Suite locations are on the [tests map](tests.md).

| Behavior | Entry point |
|---|---|
| GET and probe HTTP contracts, authorization split, warm reuse | `TrickplayPreviewHttpSpecs.cs` (ComponentTests) |
| GET outcome mapping and conditional requests | `PreviewOutcomeSpecs.cs` (UnitTests) |
| Probe outcome contract | `TrickplayFrameProbeSpecs.cs` (UnitTests) |
| Authorization-split boundary | `PreviewContextBoundarySpecs.cs` (UnitTests) |
