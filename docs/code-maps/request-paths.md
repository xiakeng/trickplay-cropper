# Request paths map

`TrickplayCropper/Videos/{itemId}/Preview` routes GET and the Frame Probe. GET resolves
an authorized image; the probe returns its Frame Index before image work. Both share the
[observation caches](caching/observations.md).

## Controller

HTTP routing lives in
[TrickplayPreviewController.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Api/TrickplayPreviewController.cs):
`GetAsync` binds the query; `HeadAsync` rejects malformed input through `TryCreateQuery`.
`MapOutcome` and `CreateBodylessResult` map the closed `PreviewOutcome` and
`TrickplayFrameProbeOutcome` sets to status, headers, and body.

GET keeps the default policy. HEAD's named policy is configured in
[TrickplayFrameProbeAuthorization.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Api/TrickplayFrameProbeAuthorization.cs):
`CustomAuthentication`, a true API-key claim or nonempty user-ID GUID, and no user lookup.

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

| Behavior | Entry point |
|---|---|
| GET responses, authorization, and failures | `TrickplayPreviewGetResponseHttpSpecs.cs`, `TrickplayPreviewAuthorizationHttpSpecs.cs`, `TrickplayPreviewFailureHttpSpecs.cs` (ComponentTests) |
| Probe HTTP, authorization, and Kestrel | `TrickplayFrameProbeHttpSpecs.cs`, `TrickplayFrameProbeAuthorizationHttpSpecs.cs`, `TrickplayPreviewKestrelHttpSpecs.cs` (ComponentTests) |
| Outcome mapping and authorization split | `PreviewOutcomeSpecs.cs`, `TrickplayFrameProbeSpecs.cs`, `PreviewContextBoundarySpecs.cs` (UnitTests) |
