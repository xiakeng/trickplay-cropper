# Trickplay Cropper Next-Stage Specification

- Status: Approved direct-index contract
- Baseline: v1.0.0.0
- Plugin ID: `630fb758-9a29-4f2c-a54c-95793651bb8a`

## Purpose

Trickplay Cropper serves authenticated JPEG previews cropped from Jellyfin-owned
Source Sprites. A playback client first requests a Frame Timeline, then requests
each selected frame directly by zero-based Frame Index. The plugin never creates
or repairs Source Sprites.

## HTTP contract

`GET /TrickplayCropper/Videos/{itemId}/FrameTimeline` accepts an optional GUID
`MediaSourceId` (defaulting to `itemId`) and returns exactly:

```json
{"intervalTicks": 100000000, "frameCount": 77}
```

The interval is a checked positive 64-bit Jellyfin-tick conversion and the count
is positive. The operation uses current-user authorization, exact source
membership and Source Video visibility, one authoritative metadata read, and no
Source Sprite, Preview Cache, conditional, or encoder work. It returns
`Cache-Control: private, no-cache` without validators.

`GET /TrickplayCropper/Videos/{itemId}/Preview` requires a 32-bit `FrameIndex`
and accepts the same optional `MediaSourceId`. Missing, malformed, overflowing,
negative, or authoritative-count-out-of-range values return `400`. Zero and the
last generated index succeed. The server does not derive or clamp an index from
playback position. Unknown query parameters retain normal ASP.NET behavior.

Both operations perform the full current-user authorization boundary on every
request. A prior Timeline, cached JPEG, or ETag never substitutes for current
authorization or validation. Userless API keys are forbidden for Preview and
Timeline.

## Resolution and metadata

Each request copies the current positive Trickplay Resolution Targets, chooses the
minimum target, clamps it to the matched Media Source video width when smaller,
normalizes to Jellyfin's required even width, and requires the exact generated
metadata key and matching frame width. Missing or concealed data is `404`;
invalid configuration or required generated data is `500`.

Preview reads generated metadata authoritatively once after authorization,
including disk-cache hits and conditional requests. It validates positive frame
and tile geometry and count, but an otherwise valid Preview accepts a non-positive
interval. Timeline requires a positive interval as well. The plugin does not
promise an atomic metadata/Source Sprite snapshot.

The supplied Frame Index is validated before `FrameSelection`, Source Sprite
lookup, checked crop arithmetic, Preview Cache access, conditional comparison, or
encoding. Selection remains row-major. Source Sprite paths remain manager-owned;
the plugin checks existence and retains accepted length plus UTC-last-write facts.

## Representation and failures

Successful Preview responses preserve `image/jpeg`, inline disposition,
`Content-Length`, opaque strong ETags, `Cache-Control: private, no-cache`,
`Server-Timing`, and applicable `X-Trickplay-Cache`. Exact, weak, and wildcard
`If-None-Match` matches return a bodyless `304` only after authorization,
authoritative metadata/index validation, and current representation identity.
`X-Trickplay-Frame-Index` is not emitted.

Preview Cache identity retains namespace, effective Media Source ID, frame and
tile dimensions, Source Sprite index and length/mtime facts, direct Frame Index,
and JPEG quality. Interval and thumbnail count are not identity inputs; count
still bounds requests. No cache migration or namespace migration is provided.

The public failure contract is `400` for malformed or out-of-range input, `401`
for authentication failure, `403` for authenticated denial, `404` for concealed
or unavailable data, and `500` for invalid server data or processing failures.
Error bodies are not a plugin-specific DTO contract.

## Harness verification

The manual Integration Harness requests one successful Frame Timeline for each
playable Item at playback start and compares its exact interval and count with an
independent Jellyfin generated-metadata read. It retains those Timeline responses
for both boundary smoke checks and the coordinated Scrub Storm; no stage requests
another Timeline for the same playback. Smoke checks exercise Frame Index `0`,
`frameCount - 1`, and `frameCount` (`400`), plus invalid-token and concealed-Item
GET behavior for Timeline and Preview.

Scrub Storm replays the existing six-lane trajectories as direct Preview GET load.
It preserves barriers, cache MISS-to-HIT transitions, stable JPEG bytes and opaque
ETags, Cache Tree and temporary-file checks, structured Debug-event reconciliation,
cancellation, deployment/restoration, and host-health checks. Its report records
GET and operational outcomes only; it intentionally omits HEAD fan-out and
performance timing statistics.

## Retired model and verification

The HEAD Frame Probe, PositionTicks selection, end clamping, Source Facts
Observation, Generated Metadata Observation, their lifetimes/order/reclamation,
and Probe policy are retired. The disk Preview Cache, crop/encoding, cleanup,
coordination, cancellation, diagnostics, and unrelated TimeProvider consumers
remain.

The HTTP component seam proves binding, authorization, authoritative reads,
range validation, Timeline schema, conditional behavior, identity stability,
crop/cache behavior, and failure mapping. The live Harness migration is owned by
this ticket: the old Harness is temporarily incompatible, and parent completion
requires migrated real-host Timeline/direct-index evidence. Business
Documentation and Code Maps remain under their existing maintenance workflows.
