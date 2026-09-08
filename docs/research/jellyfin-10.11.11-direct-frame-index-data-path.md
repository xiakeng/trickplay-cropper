# Jellyfin 10.11.11 direct Frame Index data path

Research date: 2026-09-08.

## Scope and source boundary

This note answers [issue #149](https://github.com/xiakeng/trickplay-cropper/issues/149)
for the repository state based on commit `c5c704ac68a31fa237220e8b7b61da5caa972235`.
The repository pins `Jellyfin.Controller` and `Jellyfin.Model` to `10.11.11`.
[Package pins][package-pins] External Jellyfin links are fixed to commit
`1fbd8739292cce610231be93daf43368733edf63`, the source commit for the official
`v10.11.11` tag. [Jellyfin release][jellyfin-release]
[Jellyfin source commit][jellyfin-source-commit]

The evidence is limited to the current plugin source and tests plus Jellyfin's
official source. Where those sources do not choose a product policy, this note
identifies the choice instead of inferring an answer.

## Answer

Removing the Trickplay Frame Probe and server-side `PositionTicks` selection does
not remove the server's authority boundary. A Frame Timeline request and every
direct Trickplay Preview request still need to authenticate a current user, resolve
the exact user-visible logical video, require full play access, prove that the
effective Source Video is a current playback Media Source, and resolve that Source
Video through the user's library view. A prior Frame Timeline response cannot be
used as authorization for a later Preview request. The current GET performs these
checks before generated-metadata, Source Sprite, conditional-response, or Preview
Cache work. [Authorized context][authorized-context]
[Preview orchestration][preview-orchestration]

Both operations also need one authoritative generated-metadata row for the effective
Source Video. The Frame Timeline needs the row's positive `Interval` and
`ThumbnailCount`; `intervalTicks` is the checked conversion from Jellyfin's stored
milliseconds. The direct Preview additionally needs `Width`, `Height`, `TileWidth`,
and `TileHeight`, and it must establish `0 <= FrameIndex < ThumbnailCount` before
performing tile or crop arithmetic. Jellyfin defines those fields on
`TrickplayInfo`, and its manager returns rows for one Item keyed by `Width`.
[TrickplayInfo fields][trickplay-info]
[Jellyfin metadata query][jellyfin-metadata-query]

Only a direct Preview continues to need the library's `SaveTrickplayWithMedia`
value, the manager-resolved Source Sprite path, Source Sprite existence and version
facts, crop geometry, actual JPEG/grid validation, Preview Cache identity, and ETag
comparison. A Frame Timeline returns calculation data and therefore needs none of
those representation reads. [Source Sprite resolution][source-resolution]
[Encoder validation][encoder-validation]

The Source Facts Observation is wholly obsolete once HEAD is removed: current GET
requests only publish into it, while only the Trickplay Frame Probe reads it. The
underlying current-user Item, membership, width, and Source Video reads are not
obsolete. [Source-facts cache][source-facts-cache]

The Generated Metadata Observation is only partly obsolete. Its probe-facing API,
positive 30-minute reuse, and HEAD/GET ordering contract lose their consumer. Its
Preview-facing negative reuse remains live behavior today, so deleting or reshaping
the remaining negative observation policy is a product decision. A new Frame
Timeline could become a positive-observation consumer, but no primary source requires
that design. [Metadata cache][metadata-cache]
[Metadata reuse policy][metadata-reuse]

## Verified current data path

### Authorization and source facts

The current GET route uses the ordinary `[Authorize]` policy. After the host accepts
the request, plugin code resolves the `Jellyfin-UserId` claim to a current user;
userless API keys are forbidden, and missing or unresolvable user identities are
rejected. [GET route][get-route] [Authorized context][authorized-context]

For each request, the plugin then performs the following authoritative reads in this
order:

1. Resolve the logical video by `ItemId` through `ILibraryManager` with the current
   user; a miss is concealed as not found.
2. Require `logicalVideo.GetPlayAccess(user) == PlayAccess.Full`.
3. Call `IMediaSourceManager.GetPlaybackMediaSources` with the logical video and
   current user, then require an exact GUID match for `MediaSourceId ?? ItemId`.
4. Resolve that exact Source Video through `ILibraryManager` with the current user;
   a miss is concealed.
5. Read the matched Media Source's video-stream width. The current Selected Trickplay
   Resolution policy uses this width to normalize the chosen Trickplay Resolution
   Target. [Authorized context][authorized-context]

The first four reads are required by the already-approved full current-user boundary.
[Parent map][parent-map]
The fifth is required only if the new contract preserves the current resolution
selection policy. Jellyfin itself exposes generated rows rather than selecting one
for this plugin; preserving the minimum-target, even-width, source-width-clamped
selection is a product choice. Jellyfin's generator does perform the same even-width
and source-width normalization when it creates rows. [Resolution resolver][resolution-resolver]
[Jellyfin width normalization][jellyfin-width-normalization]

The Frame Timeline and every direct Preview need to repeat the authorization and
membership path. The Frame Timeline may omit the later representation reads, but it
must not issue reusable authority that lets a subsequent Preview skip current-user
checks.

### Generated metadata

The current resolver reads current global `TrickplayOptions.WidthResolutions`, derives
one Selected Trickplay Resolution, and calls
`ITrickplayManager.GetTrickplayResolutions(effectiveSourceVideoId)`. It requires an
exact dictionary entry and copies `Width`, `Height`, `Interval`, `TileWidth`,
`TileHeight`, and `ThumbnailCount` into immutable plugin metadata. It treats an empty
dictionary, a missing selected width, and non-positive thumbnail count as unavailable;
all other fields must be positive and the row's `Width` must match its key.
[Resolution resolver][resolution-resolver] [Metadata cache][metadata-cache]
[Metadata validation][metadata-validation]

Jellyfin 10.11.11 stores `Interval` in milliseconds and `ThumbnailCount` as the total
number of non-black thumbnails. Therefore the two compact Frame Timeline facts are:

```text
intervalTicks = checked((long)Interval * TimeSpan.TicksPerMillisecond)
frameCount    = ThumbnailCount
```

The conversion matches the current server-side selection arithmetic. The Timeline
must not derive `frameCount` from runtime or from `TileWidth * TileHeight`: the last
Source Sprite can be partial, and Jellyfin records the actual thumbnail count.
[Metadata validation and selection][metadata-validation]
[Jellyfin tile creation][jellyfin-tile-creation]

The Frame Timeline needs no `Bandwidth`, Source Sprite path, Source Sprite stat,
crop coordinates, encoded bytes, or Preview Cache identity. Those facts do not affect
`intervalTicks` or `frameCount`.

### Direct Frame Index validation and crop geometry

`FrameIndex` becomes untrusted request input when the server stops deriving it from a
non-negative position. The current path is safe only because `SelectFrameIndex`
clamps the calculated value to `ThumbnailCount - 1`. `FrameSelection.Create` itself
does not reject a negative or too-large index before division and modulo.
[Metadata validation and selection][metadata-validation]
[Crop selection][crop-selection]

The direct request therefore needs a trust-boundary check before
`FrameSelection.Create`:

```text
0 <= FrameIndex && FrameIndex < ThumbnailCount
```

Whether an out-of-range value maps to `400`, `404`, or another documented response is
an HTTP policy choice. Silently applying the old PositionTicks clamp would turn a
caller-supplied invalid identity into a different requested frame and is not required
by any primary source.

After validation, the remaining geometry is deterministic:

```text
framesPerSprite = checked(TileWidth * TileHeight)
spriteIndex     = FrameIndex / framesPerSprite
cellIndex       = FrameIndex % framesPerSprite
row             = cellIndex / TileWidth
column          = cellIndex % TileWidth
cropX           = checked(column * Width)
cropY           = checked(row * Height)
cropWidth       = Width
cropHeight      = Height
```

The current implementation keeps the products in checked 64-bit arithmetic and
rejects coordinates that do not fit Skia's integer boundary. Those geometry and
overflow checks remain applicable to direct Frame Index requests.
[Crop selection][crop-selection] [Crop tests][crop-tests]

### Tile path and non-atomic observation boundary

For a direct Preview, the plugin reads `SaveTrickplayWithMedia` for the selected
Source Video and calls Jellyfin's `GetTrickplayTilePathAsync` with that Source Video,
the selected row's frame width, and `spriteIndex`. The storage option selects whether
the manager uses media-adjacent or server metadata storage.
[Source Sprite resolution][source-resolution]
[Jellyfin manager interface][jellyfin-manager-interface]
[Jellyfin library option][jellyfin-library-option]

Jellyfin 10.11.11's tile-path method performs another
`GetTrickplayResolutions(item.Id)` call. It uses the newly read row's `TileWidth` and
`TileHeight` to construct the directory, then appends `{spriteIndex}.jpg`; when the
requested width is no longer present it returns an empty path.
[Jellyfin tile path][jellyfin-tile-path]

Consequently, the first generated-metadata read does not pin the later path lookup.
The plugin calculates crop geometry from the first row, while Jellyfin may calculate
the directory from a second row. The current plugin then separately checks file
existence and captures length and last-write ticks. It does not obtain an atomic
metadata/Source Sprite generation snapshot. No pinned Jellyfin interface exposes such
a snapshot or version token, and the parent map explicitly keeps regeneration during
playback and a Timeline Version out of scope. [Source Sprite resolution][source-resolution]
[Parent map][parent-map]

The encoder still must open the file as JPEG, require positive actual dimensions,
require the actual Source Sprite dimensions to equal `TileWidth * Width` by
`TileHeight * Height`, and require the crop rectangle to remain inside those bounds.
Those checks prevent a caller-supplied index or mismatched Sprite from reaching an
out-of-bounds decode. [Encoder validation][encoder-validation]

### Preview Cache identity and conditional responses

The current identity hashes this canonical source/encoding state:

- namespace version and effective Media Source ID;
- frame width and height, interval, tile width and height, and thumbnail count;
- Source Sprite index, length, and UTC last-write ticks; and
- JPEG quality.

The ETag and cache path then add the zero-padded Frame Index. The path is separated by
effective Media Source, frame width, Source Sprite identity, and Frame Index.
[Preview identity][preview-identity]

For the direct-index representation, the fields that participate in addressing or
creating bytes still include the effective Media Source, frame width and height, tile
geometry, Source Sprite index/version, Frame Index, encoding version/quality, and cache
namespace. The Source Sprite version mechanism must remain sufficient to invalidate a
changed source before a `304`; the current accepted mechanism is length plus last-write
ticks rather than a Jellyfin generation token. [Preview identity][preview-identity]

`Interval` no longer participates in crop or encoding once Frame Index is supplied.
`ThumbnailCount` participates in request bounds but not in crop or encoding after a
valid index is established. Their continued inclusion in the ETag/cache digest would
be conservative invalidation policy, not a byte-generation requirement. The primary
sources do not decide whether to keep them.

The controller parses `If-None-Match` early, but the comparison occurs only after the
full authorization path, generated-metadata resolution, tile-path lookup, Source
Sprite existence check, file stat, crop selection, and identity creation. A wildcard
or weakly matching tag returns `304` before Preview Cache access or encoding; otherwise
the same identity enters the coordinated disk cache and encoder.
[GET route][get-route] [Preview orchestration][preview-orchestration]

That ordering remains the safe current conditional-response boundary. A `304` cannot
be returned from a Timeline-era client token alone, because the server still must
authorize the present request and establish the current representation identity. The
exact ETag schema and whether to preserve weak comparison are product-contract choices.

## Obsolete and surviving implementation boundaries

| Boundary | Evidence-based disposition after HEAD and PositionTicks removal |
| --- | --- |
| `PreviewQueryParameters.PositionTicks`, `PreviewQuery.PositionTicks`, negative-position checks | Obsolete. Replace with the chosen direct `FrameIndex` binding and validate it against the authoritative row. [Query binding][query-binding] [Preview query][preview-query] |
| `TrickplayMetadata.SelectFrameIndex` and the frame-calculation resolver's PositionTicks selection | Obsolete on the server. The client performs timeline-to-index calculation. Metadata selection/validation still needs a server boundary. [Metadata validation and selection][metadata-validation] [Resolution resolver][resolution-resolver] |
| Controller HEAD action, raw HEAD parsing, bodyless outcome mapping, HEAD headers | Obsolete in full. [GET and HEAD route][get-route] |
| `ITrickplayFrameProbe`, `TrickplayFrameProbe`, `TrickplayFrameProbeOutcome`, and `ITrickplayFrameProbeContextResolver`/implementation | Obsolete in full. They exist only to answer HEAD. [Frame Probe][frame-probe] [Probe context][probe-context] |
| Named `TrickplayFrameProbe` authorization policy and its DI registrations | Obsolete. The new operations use the already-approved full current-user boundary. GET's ordinary authorization remains. [Service registration][service-registration] |
| `TrickplaySourceFactsCache`, its resolution types, Preview publication scope, lifetimes, ordering, and user-independent host reads | Obsolete in full. GET only publishes and HEAD is the only reader. Remove the cache wrapper, not GET's current-user manager reads. [Source-facts cache][source-facts-cache] |
| `ResolveForProbeAsync`, metadata `GetForProbeAsync`, `MetadataAccess.Probe`, and positive 30-minute reuse | Obsolete unless deliberately repurposed for Frame Timeline. There is no remaining consumer in the current GET-only design. [Resolution resolver][resolution-resolver] [Metadata cache][metadata-cache] [Metadata reuse policy][metadata-reuse] |
| Preview negative generated-metadata observations | Not automatically obsolete. Current GET reuses only negative observations; whether Frame Timeline and direct Preview retain, share, or remove them is a policy decision. [Metadata reuse policy][metadata-reuse] |
| `FrameSelection.Create`, Source Sprite resolver, encoder, disk cache, and cache coordination | Survive. Direct Frame Index starts at the existing geometry boundary after range validation. [Crop selection][crop-selection] [Source Sprite resolution][source-resolution] |
| GET authorization and concealment | Survive for both new endpoints. A Timeline response is calculation data, not future authorization evidence. [Authorized context][authorized-context] |

`ReclaimingSourceCollection` is shared infrastructure for Source Facts and Generated
Metadata state. Removing Source Facts does not by itself make that helper obsolete if
the remaining metadata observation design still uses it.

## Test migration boundary

The following tests become obsolete rather than needing semantic preservation:

- all of `TrickplayFrameProbeSpecs.cs` and `TrickplayFrameProbeHttpSpecs.cs`;
- the Frame Probe Kestrel cases and `TrickplayFrameProbeKestrelCondition`;
- all of `TrickplaySourceFactsObservationHttpSpecs.cs`;
- the probe-isolation and shared HEAD/GET assertions in
  `PreviewContextBoundarySpecs.cs` and `PreviewObservationSpecs.cs`; and
- the HEAD phase, HEAD assertions, HEAD request counts, and HEAD response-time
  expectations in the Integration Harness.

[Probe unit tests][probe-unit-tests] [Probe HTTP tests][probe-http-tests]
[Source observation tests][source-observation-tests]
[Context boundary tests][context-boundary-tests]
[Observation unit tests][observation-unit-tests]
[Harness request][harness-request] [Harness storm][harness-storm]

The following tests remain valuable but need the wire/input boundary rewritten:

- GET authorization, concealment, current membership, Source Sprite, conditional
  response, disk cache, and encoder tests remain; replace PositionTicks inputs with
  direct Frame Index inputs and add explicit negative/upper-bound rejection.
- `FrameSelectionSpecs` remains the crop-geometry and checked-arithmetic boundary.
- `TrickplayMetadataSpecs` keeps positive metadata validation, but its PositionTicks
  selection/clamping tests become obsolete; new Frame Timeline tests should prove the
  checked interval conversion and exact frame count instead.
- `PreviewIdentitySpecs` remains, but expected canonical fields must follow the later
  decision on whether interval and thumbnail count remain conservative identity inputs.
- Generated Metadata Observation HTTP tests that only prove HEAD positive reuse or
  HEAD/GET ordering become obsolete. Tests for Preview negative reuse, newer-read
  ordering, cancellation settlement, and source scoping survive only if that policy is
  retained.
- The real-Jellyfin Harness should fetch one Frame Timeline per playback subject and
  send direct Frame Index GETs. Its JPEG, ETag, repeated-byte, disk-cache,
  coordination, and Source Sprite checks remain applicable; the 864-request HEAD
  fan-out is obsolete.

[GET response tests][get-response-tests]
[Authorization tests][authorization-tests]
[Crop tests][crop-tests] [Metadata tests][metadata-tests]
[Identity tests][identity-tests]
[Generated observation tests][generated-observation-tests]
[Generated scope tests][generated-scope-tests]

## Product-policy choices left for #150 and #151

Primary sources establish the required facts and safety boundaries above, but do not
decide:

1. the public Frame Timeline schema and exact error status for invalid or out-of-range
   Frame Index input;
2. whether the new contract preserves current minimum-target normalization or chooses
   a generated row by a different explicit rule;
3. whether Frame Timeline and Preview perform authoritative metadata reads on every
   request, share positive observations, retain negative observations, or use no
   generated-metadata cache;
4. whether a direct Preview still returns `X-Trickplay-Frame-Index` when the request
   already identifies that index;
5. whether `Interval` and `ThumbnailCount` remain conservative Preview Cache/ETag
   identity fields even though they no longer determine bytes after bounds validation;
6. the exact ETag format, weak-comparison behavior, and conditional revalidation
   contract; or
7. any coherence promise across the Frame Timeline read, a later direct Preview read,
   Jellyfin's internal tile-path metadata reread, and Source Sprite replacement.

No pinned Jellyfin 10.11.11 primary source provides a Timeline Version or atomic
metadata/Source Sprite snapshot. Adding either would be a new product protocol and is
outside the parent map's stated scope.

[package-pins]: ../../Directory.Packages.props
[parent-map]: https://github.com/xiakeng/trickplay-cropper/issues/148
[jellyfin-release]: https://github.com/jellyfin/jellyfin/releases/tag/v10.11.11
[jellyfin-source-commit]: https://github.com/jellyfin/jellyfin/commit/1fbd8739292cce610231be93daf43368733edf63
[query-binding]: ../../src/Jellyfin.Plugin.TrickplayCropper/Api/PreviewQueryParameters.cs
[get-route]: ../../src/Jellyfin.Plugin.TrickplayCropper/Api/TrickplayPreviewController.cs
[preview-query]: ../../src/Jellyfin.Plugin.TrickplayCropper/Preview/PreviewQuery.cs
[authorized-context]: ../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewContextResolver.cs
[resolution-resolver]: ../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinTrickplayFrameCalculationResolver.cs
[metadata-cache]: ../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/TrickplayMetadataCache.cs
[metadata-reuse]: ../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/GeneratedMetadataObservationState.cs
[metadata-validation]: ../../src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayMetadata.cs
[source-facts-cache]: ../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/TrickplaySourceFactsCache.cs
[probe-context]: ../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinTrickplayFrameProbeContextResolver.cs
[frame-probe]: ../../src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayFrameProbe.cs
[source-resolution]: ../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewSourceResolver.cs
[crop-selection]: ../../src/Jellyfin.Plugin.TrickplayCropper/Preview/FrameSelection.cs
[preview-identity]: ../../src/Jellyfin.Plugin.TrickplayCropper/Preview/PreviewIdentity.cs
[preview-orchestration]: ../../src/Jellyfin.Plugin.TrickplayCropper/Preview/TrickplayPreview.cs
[encoder-validation]: ../../src/Jellyfin.Plugin.TrickplayCropper/Imaging/TrickplayPreviewEncoder.cs
[service-registration]: ../../src/Jellyfin.Plugin.TrickplayCropper/PluginServiceRegistrator.cs
[jellyfin-manager-interface]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Trickplay/ITrickplayManager.cs#L39-L95
[jellyfin-metadata-query]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L494-L515
[jellyfin-tile-path]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L537-L544
[jellyfin-width-normalization]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L239-L276
[jellyfin-tile-creation]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L319-L359
[trickplay-info]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/TrickplayInfo.cs#L6-L73
[jellyfin-library-option]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Configuration/LibraryOptions.cs#L105-L109
[probe-unit-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.UnitTests/TrickplayFrameProbeSpecs.cs
[probe-http-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests/TrickplayFrameProbeHttpSpecs.cs
[source-observation-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests/TrickplaySourceFactsObservationHttpSpecs.cs
[context-boundary-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.UnitTests/PreviewContextBoundarySpecs.cs
[observation-unit-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.UnitTests/PreviewObservationSpecs.cs
[get-response-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests/TrickplayPreviewGetResponseHttpSpecs.cs
[authorization-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests/TrickplayPreviewAuthorizationHttpSpecs.cs
[crop-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.UnitTests/FrameSelectionSpecs.cs
[metadata-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.UnitTests/TrickplayMetadataSpecs.cs
[identity-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.UnitTests/PreviewIdentitySpecs.cs
[generated-observation-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests/TrickplayGeneratedMetadataObservationHttpSpecs.cs
[generated-scope-tests]: ../../tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests/TrickplayGeneratedMetadataScopeHttpSpecs.cs
[harness-request]: ../../tools/TrickplayCropper.IntegrationHarness/PreviewRequest.cs
[harness-storm]: ../../tools/TrickplayCropper.IntegrationHarness/ScrubStorm.cs
