# Request paths map

`TrickplayCropper/Videos/{itemId}/Preview` routes GET and the Frame Probe. GET returns a
Preview; the probe returns its Frame Index. Both share the
[observation caches](caching/observations.md).

## Controller

HTTP routing lives in
[TrickplayPreviewController.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Api/TrickplayPreviewController.cs):
`GetAsync` retains default authorization; `HeadAsync` uses the named `TrickplayFrameProbe`
native-identity policy. Mapping methods turn their closed outcomes into HTTP responses.

## GET chain

`GetAsync` → `TrickplayPreview.ProcessAsync` → `JellyfinPreviewContextResolver.ResolveAsync` →
`ResolveForPreviewAsync` (Selected Trickplay
Resolution and Frame Index) → `JellyfinPreviewSourceResolver.ResolveAsync` (Source Sprite
snapshot) → `PreviewIdentity.Create` → conditional `If-None-Match` check →
`DiskPreviewCache.GetOrCreateAsync` → `TrickplayPreviewEncoder.EncodeAsync` → outcome.

| Target | Key symbols | Responsibility |
|---|---|---|
| [TrickplayPreview.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayPreview.cs) | `ProcessAsync`, `PreviewFailureLog` | GET orchestration; maps unexpected failures to one redacted Error |
| [JellyfinPreviewContextResolver.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewContextResolver.cs) | `ResolveAsync` | User-scoped authorization with concealment; publishes only user-verified source facts |
| [ClaimsPrincipalExtensions.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/ClaimsPrincipalExtensions.cs) | `TryGetJellyfinUserId`, `IsJellyfinApiKey` | Claim-only native identity parsing shared by GET and probe authorization |
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
| Probe HTTP contract, authorization, and warm reuse | `TrickplayFrameProbeAuthorizationHttpSpecs.cs`, `TrickplayFrameProbeHttpSpecs.cs` (ComponentTests) |
| Real Kestrel wiring | `TrickplayPreviewKestrelHttpSpecs.cs` (ComponentTests) |
| GET outcome mapping and conditional requests | `PreviewOutcomeSpecs.cs` (UnitTests) |
| Probe outcome contract | `TrickplayFrameProbeSpecs.cs` (UnitTests) |
| Authorization-split boundary | `PreviewContextBoundarySpecs.cs` (UnitTests) |
