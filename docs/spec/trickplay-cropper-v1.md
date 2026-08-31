# Trickplay Cropper v1 Specification

- Status: Approved in GitHub issue #12
- Implementation target: v1.0.0.0
- Plugin ID: `630fb758-9a29-4f2c-a54c-95793651bb8a`

## 1. Purpose and scope

Trickplay Cropper is an authenticated Jellyfin server plugin. It selects one
frame from a Jellyfin-owned trickplay Source Sprite, crops that frame with
SkiaSharp, encodes it as JPEG, caches the result in Jellyfin temporary storage,
and returns it over HTTP.

v1 includes only the Jellyfin server plugin. It does not:

- generate or repair Jellyfin trickplay data;
- change Kodi or any other client;
- provide a settings page or a plugin-specific authentication token;
- support more than Jellyfin Server 10.11.11;
- publish a Jellyfin catalog or update repository;
- define a separate operating-system or CPU support matrix; or
- detect or compensate for inconsistent Jellyfin trickplay frame counts.

## 2. Compatibility and package contract

The production assembly is `Jellyfin.Plugin.TrickplayCropper.dll` and targets
`net9.0`. Production package references are pinned as follows:

- `Jellyfin.Controller` 10.11.11;
- `Jellyfin.Model` 10.11.11; and
- `SkiaSharp` 3.116.1 as a compile-time reference supplied at runtime by the
  Jellyfin host.

The target Jellyfin plugin ABI is `10.11.0.0`. The plugin name is
`Trickplay Cropper`; its ID is the GUID stated above. Jellyfin discovers the
public plugin, Controller, scheduled-task, and service-registrator types. All
other production types are internal unless a Jellyfin discovery contract
requires otherwise. The test assemblies use `InternalsVisibleTo` and still
exercise modules through their intended interfaces.

The manually installable package is a flat ZIP containing exactly:

```text
Jellyfin.Plugin.TrickplayCropper.dll
meta.json
```

It contains no PDB, nested directory, `Jellyfin.*` assembly, `SkiaSharp*`
assembly, native library, NativeAssets package, or `runtimes` tree. JPRM
generates `meta.json` and the ZIP for the production project only.

## 3. HTTP contract

### 3.1 Request

```http
GET /TrickplayCropper/Videos/{ItemId}/Preview
    ?MediaSourceId={MediaSourceId}
    &PositionTicks={PositionTicks}
```

| Parameter | Location | Type | Required | Rule |
| --- | --- | --- | --- | --- |
| `ItemId` | Route | `Guid` | yes | Logical video currently being played |
| `MediaSourceId` | Query | `Guid` | no | Defaults to `ItemId`; required by the caller for an alternate version |
| `PositionTicks` | Query | `Int64` | yes | Jellyfin ticks; must be non-negative |

There is no `Width` parameter. v1 always requires the exact 320px entry from
Jellyfin trickplay metadata. Authentication uses Jellyfin's standard
mechanism, including an existing `X-Emby-Token`; the plugin adds no token
parameter.

The Controller is marked with `[ApiController]` and `[Authorize]`.

### 3.2 Successful response

A generated or cached preview returns:

```http
HTTP/1.1 200 OK
Content-Type: image/jpeg
Content-Length: ...
Content-Disposition: inline
ETag: "{sourceStamp}-f{frameIndex:D10}"
Cache-Control: private, no-cache
X-Trickplay-Cache: HIT|MISS
Server-Timing: ...
```

`Server-Timing` reports only stages that occurred, selected from `lookup`,
`cache`, `decode`, and `encode`. Clients must not depend on diagnostic headers
for behavior. The plugin never emits `X-Trickplay-Cache-File`.

The endpoint supports `If-None-Match`. A match returns a bodyless `304 Not
Modified` after authorization and Source Sprite validation but before plugin
cache access or decode-gate acquisition. The response includes `ETag`,
`Cache-Control`, and timings for completed stages, but no
`X-Trickplay-Cache` value.

### 3.3 Failure response

| Status | Condition |
| --- | --- |
| `400` | Missing or malformed request parameters, or negative `PositionTicks` |
| `401` | Missing, invalid, or unusable user-session authentication |
| `403` | Default authorization-policy denial, authenticated API key without a current user, or explicit playback denial on an otherwise visible video |
| `404` | Missing, hidden, or wrong-type logical/source item; non-member source; missing exact 320px metadata; no thumbnails; missing manager path; or missing Source Sprite |
| `500` | Invalid or contradictory trusted metadata, checked arithmetic failure, cache safety failure, decode/encode failure, or other unexpected processing failure |

Expected errors use the normal Jellyfin/ASP.NET JSON error shape. The plugin
never returns a blank placeholder and never caches a failure. Request
cancellation propagates rather than being converted to an outcome.

## 4. Authorization and Source Sprite resolution

The effective source ID is:

```text
resolvedMediaSourceId = MediaSourceId ?? ItemId
```

The resolver performs the following work in order:

1. Require a current authenticated Jellyfin user; an API key without a user is
   forbidden.
2. Resolve the logical item with the user-scoped
   `ILibraryManager.GetItemById<Video>(..., user)` overload.
3. Require `PlayAccess.Full` for the visible logical video.
4. Enumerate `IMediaSourceManager.GetPlaybackMediaSources(...)` for that video
   and prove that the selected source ID is a member. Parse
   `MediaSourceInfo.Id` as a GUID before comparison.
5. Resolve the selected source video with the same user-scoped library lookup
   and require full playback access.
6. Call `ITrickplayManager.GetTrickplayResolutions(resolvedMediaSourceId)` and
   require the exact key `320`.
7. Validate metadata and select the frame, sprite, cell, and crop.
8. Obtain the effective source item's `SaveTrickplayWithMedia` setting and call
   `ITrickplayManager.GetTrickplayTilePathAsync(resolvedItem, 320,
   spriteIndex, saveWithMedia)`.
9. Require the returned Source Sprite file to exist, then capture its `Length`
   and `LastWriteTimeUtc.Ticks`.

The plugin never reconstructs Jellyfin's Source Sprite path. Every identity,
ETag, and shared Preview Cache Entry operation occurs only after all user,
visibility, playback, membership, metadata, path, and file checks above.
Unavailable or non-member resources are concealed as `404`.

## 5. Frame and crop selection

Jellyfin metadata is interpreted as follows:

- `Width` and `Height`: one preview frame's dimensions;
- `TileWidth` and `TileHeight`: Source Sprite columns and rows;
- `Interval`: milliseconds between frames; and
- `ThumbnailCount`: authoritative total frame count.

`ThumbnailCount <= 0` is treated as no available preview. Other non-positive
dimensions or interval values and all checked arithmetic failures are internal
metadata errors.

All products and conversions use checked `Int64` arithmetic before conversion
to narrower Skia or path-formatting values. Selection uses integer division:

```text
ticksPerFrame  = intervalMs * 10,000
rawFrameIndex  = PositionTicks / ticksPerFrame
frameIndex     = min(rawFrameIndex, thumbnailCount - 1)

framesPerSprite = tileWidth * tileHeight
spriteIndex     = frameIndex / framesPerSprite
cellIndex       = frameIndex % framesPerSprite
row             = cellIndex / tileWidth
column          = cellIndex % tileWidth

cropX      = column * frameWidth
cropY      = row * frameHeight
cropWidth  = frameWidth
cropHeight = frameHeight
```

A position inside an interval selects its first frame. A position after the
last metadata-defined frame clamps to the last frame. Cells are row-major.

v1 assumes Jellyfin's metadata is correct. It does not probe undeclared sprite
indexes, scan storage, infer a replacement `ThumbnailCount`, add inconsistency
diagnostics, or provide recovery instructions. Incorrect upstream counts may
therefore clamp early and return an incorrect preview.

## 6. Preview Identity, ETag, and Cache Tree

After the Source Sprite snapshot is captured, construct this exact UTF-8
canonical string. GUIDs use lowercase `N` format and numbers use invariant
decimal formatting:

```text
preview-v1
mediaSourceId={mediaSourceId:N}
width={frameWidth}
height={frameHeight}
intervalMs={intervalMs}
tileWidth={tileWidth}
tileHeight={tileHeight}
thumbnailCount={thumbnailCount}
spriteIndex={spriteIndex}
spriteLength={spriteLength}
spriteLastWriteUtcTicks={spriteLastWriteUtcTicks}
jpegQuality=90
```

`sourceStamp` is the first 16 bytes of the SHA-256 digest, formatted as 32
lowercase hexadecimal characters. The entity tag is:

```text
"{sourceStamp}-f{frameIndex:D10}"
```

The Cache Tree root and final Preview Cache Entry are:

```text
{TempDirectory}/Jellyfin.Plugin.TrickplayCropper/preview-v1/
  {mediaSourceId:N}/
    w{frameWidth:D4}/
      s{spriteIndex:D6}-{sourceStamp}/
        f{frameIndex:D10}.jpg
```

Identity and paths exclude `UserId`, logical `ItemId`, titles, user-provided raw
strings, media paths, and Source Sprite paths. Preview Cache Entries may be
shared between users, but authorization is never shared or bypassed. Any
future cache layout, crop rule, or output encoding change increments the
`preview-v1` namespace.

The file snapshot is captured once for each request. v1 neither locks the
Jellyfin-owned Source Sprite nor revalidates its fingerprint after encoding. A
concurrent Jellyfin replacement may make the in-flight request fail or serve
the file version it opened; a later request observes a changed length or mtime
as a new identity. Replacements that preserve both fingerprint fields are not
distinguished.

## 7. Image processing

The only v1 image path is the SkiaSharp 3.116.1 JPEG horizontal-subset
scanline path:

1. Wait asynchronously for one of four process-wide decode permits, observing
   request cancellation.
2. Create a fresh `SKCodec` while holding the permit and require JPEG input.
3. Read actual image dimensions. Require positive dimensions, exact equality
   with `tileWidth * frameWidth` by `tileHeight * frameHeight` using checked
   arithmetic, and a crop rectangle wholly inside the image.
4. Start scanline decoding with a full-height horizontal subset covering
   `[cropX, cropX + cropWidth)`.
5. Skip to `cropY` and read exactly `cropHeight` rows into one cell-sized bitmap.
   Each native skip/read call handles at most 64 scanlines, with cancellation
   checks before and after every batch.
6. Check cancellation before encoding, then encode JPEG quality 90 directly to
   the cache-owned temporary output stream. Do not allocate an intermediate
   `SKData`/`byte[]` representation.
7. Dispose every Skia object deterministically before releasing the permit.

Native work runs synchronously on the request worker and is not wrapped in
`Task.Run`. A native call or JPEG encode already in progress cannot be
interrupted.

There is no full-width scanline retry, full-Sprite bitmap decode,
`GetPixels` fallback, incremental-decode fallback, or placeholder. Rejected
subset setup, non-JPEG input, dimension mismatch, out-of-bounds crop, failed or
short skip/read, allocation failure when recoverable, destination failure, and
encode failure return `500` and publish nothing.

The plugin deliberately trusts Jellyfin-owned Source Sprites and metadata. It
sets no plugin-specific caps on source dimensions, source pixels, destination
bytes, encoded preview bytes, source file length, CPU time, compressed bytes,
or wall-clock time. It guarantees only that it does not allocate a complete
decoded Source Sprite bitmap. JPEG entropy parsing remains sequential and may
process data preceding the crop. Managed or native resource exhaustion may
affect the Jellyfin process.

## 8. Request and cache state machine

The request module executes this fixed order:

1. authenticate and authorize;
2. resolve logical video and selected source;
3. verify source membership, playback access, and exact 320px metadata;
4. select the frame and resolve/snapshot the manager-owned Source Sprite;
5. create Preview Identity and ETag;
6. evaluate `If-None-Match`; and
7. read or generate the Preview Cache Entry.

Each Preview Cache Entry is coordinated by a process-local keyed lock whose key
is its canonical absolute final JPEG path. Registry entries are reference
counted across owners and waiters and are removed after the last participant
leaves. Strict FIFO order is not an external contract, but queued cleanup must
not starve.

A request takes a shared Cache Tree lease before the entry lock, holds both
until the response is buffered, and then releases both. It never holds an entry
lock while waiting for an exclusive tree lease. Different entries can run
concurrently; identical entries never encode concurrently.

Under the entry lock:

- an existing final file is copied fully into an immutable response buffer
  before releasing disk ownership and returns `HIT`;
- a file that disappears during the read loops to MISS generation without
  releasing the entry lock;
- a MISS creates `f{frameIndex:D10}.{randomGuid:N}.tmp` in the final file's
  directory and gives its stream to the encoder;
- after a complete JPEG is written, the cache closes the stream, checks
  cancellation, rechecks the final path, and publishes with a same-directory
  atomic no-overwrite move;
- if another complete winner exists, the request deletes its temporary file,
  buffers the winner, and returns that representation;
- the final file is never overwritten; and
- the response bytes for a successfully published entry are immutable before
  the lock is released and the request returns `MISS`.

Cancellation while waiting removes the waiter. Cancellation or failure during
generation closes and deletes that request's temporary file. Cancellation
after atomic publication keeps the valid shared final file and abandons only
the response. If a generating owner is cancelled, a waiter may perform a later
sequential generation.

All Cache Tree paths are produced from structured plugin data, normalized with
`Path.GetFullPath`, and verified beneath the canonical root. Path identity is
`OrdinalIgnoreCase` on Windows and `Ordinal` elsewhere. Requests fail with
`500` if a cache path crosses a symlink or reparse point.

Coordination is intentionally single-process. There is no cross-process mutex
or startup cleanup protocol; atomic no-overwrite publication is the final
guard against outside activity.

## 9. Scheduled cleanup

The public Jellyfin `IScheduledTask` is presented as:

```text
Name: Clear Trickplay Cropper Cache
Key: ClearTrickplayCropperCache
Category: Maintenance
Description: Deletes cached previews and orphaned temporary files created by Trickplay Cropper.
```

Its default daily trigger is server-local 03:00. Jellyfin's Dashboard remains
responsible for persisting operator trigger changes. The task forwards
cancellation and progress to the cache module.

Only `.jpg` and `.tmp` files beneath
`{TempDirectory}/Jellyfin.Plugin.TrickplayCropper/` may be deleted. The task
never deletes the whole Jellyfin temporary directory, Source Sprites, unknown
files, or anything reached through a symlink/reparse point.

One cancellable mutex serializes overlapping cleanup runs. A run:

1. captures `cleanupStartedUtc` once and streams traversal rather than
   materializing an unbounded candidate list;
2. considers only `.jpg` and `.tmp` files whose `LastWriteTimeUtc` is at or
   before that boundary;
3. records canonical path, kind, `Length`, and `LastWriteTimeUtc.Ticks`;
4. for a final JPEG or standard temporary name, takes the corresponding entry
   lock, re-reads the fingerprint, and skips a disappeared or changed file;
5. maps a standard temporary name to its sibling final JPEG lock;
6. removes an unparseable temporary file only while holding an exclusive Cache
   Tree lease;
7. deletes files sequentially; and
8. revisits directories bottom-up, taking a brief writer-preferred exclusive
   Cache Tree lease for each empty-directory deletion attempt.

File cleanup uses an entry lock but no exclusive Cache Tree lease. Directory
pruning holds no entry lock. Pending exclusive pruning must not starve.
Unknown files remain and prevent their parent directory from being removed.
Files created after the fixed run boundary may remain until the next run.

The cleanup traversal skips file and directory symlinks/reparse points with a
Warning. A reparse-point root aborts the run. Missing paths are normal races;
other per-file or per-directory failures log a Warning and do not stop the run.
Cancellation completes the current indivisible filesystem operation, releases
ownership, stops scanning, and skips remaining pruning.

The task emits one final Information summary with:

```text
DeletedFiles
DeletedDirectories
FailedFiles
FailedDirectories
SkippedChangedFiles
ElapsedMilliseconds
Cancelled
```

## 10. Module and lifetime contract

Production remains one project and one assembly, organized under `Api`,
`Preview`, `Jellyfin`, `Caching`, `Imaging`, and `Tasks`.

### 10.1 Request module

`ITrickplayPreview.GetAsync` accepts a normalized `PreviewQuery`, current
`ClaimsPrincipal`, parsed conditional entity tags, and cancellation token. It
returns the closed `PreviewOutcome` set: `Ok`, `NotModified`, `BadRequest`,
`Unauthorized`, `Forbidden`, `NotFound`, or `InternalError`.

Expected 4xx/304 paths are values. `OperationCanceledException` propagates.
The deep request module owns workflow ordering and the single structured log
for each request-level `500`. The thin Controller owns only binding and mapping
the outcome to HTTP status, headers, and body.

### 10.2 Jellyfin seam and pure values

`IPreviewSourceResolver` is the only seam over Jellyfin managers and file
snapshot resolution. Its concrete implementation returns a typed
`ResolvedPreviewSource` only after all policies have passed. Do not create
pass-through interfaces for individual Jellyfin managers.

`FrameSelection.Create` and `PreviewIdentity.Create` are deterministic,
no-I/O value modules. They receive no injected interface.

### 10.3 Cache and image seams

`IPreviewCache` exposes `GetOrCreateAsync` and `ClearAsync`. `DiskPreviewCache`
owns paths, locks, leases, temporary files, publication, response buffering,
cleanup, pruning, and telemetry. It supplies a same-directory output stream to
the writer callback and returns immutable bytes plus `HIT`/`MISS` disposition.
It uses the real filesystem and an injected .NET `TimeProvider`; v1 introduces
neither `IFileSystem` nor `IClock`.

`ITrickplayPreviewEncoder.EncodeAsync` accepts a resolved source, the
cache-owned destination stream, and cancellation token. The concrete encoder
owns the gate, codec, bitmap, batch policy, quality, disposal, and decode/encode
timings. It does not close the destination, publish files, or manage cache
identity.

Do not introduce separate codec, bitmap, JPEG, decode-gate, or fallback
interfaces.

### 10.4 Registration and telemetry

`ITrickplayPreview`, `IPreviewSourceResolver`, `IPreviewCache`, and
`ITrickplayPreviewEncoder` are registered as singletons through a public
`IPluginServiceRegistrator`. Request-specific state is method data. Singleton
lifetimes make entry coordination, cleanup coordination, and the decode gate
process-wide.

`PreviewTelemetry` carries lookup, cache, decode, and encode durations plus
cache disposition. The Controller only formats applicable headers. Cache
cleanup owns per-item Warnings and its final Information summary; internal
request modules return typed failure details instead of logging duplicates.

Request failure diagnostics include, when the value is available,
`MediaSourceId`, frame and sprite indexes, source length, actual dimensions,
crop rectangle, `SUBSET` path, Skia result, failed validation/value, and elapsed
time. They never include tokens, credentials, media paths, Source Sprite paths,
or cache paths.

## 11. Verification contract

GitHub Actions runs on pull requests to and pushes on `main` using Ubuntu 24.04
and .NET SDK 9.0.x. Actions and tools are pinned to exact versions or full
commit SHAs. All projects commit `packages.lock.json`; CI restores in locked
mode.

Every stage blocks merging, in this order:

1. locked restore;
2. Release build with warnings as errors;
3. all unit tests;
4. all component tests;
5. JPRM packaging of the production project;
6. ZIP contract validation; and
7. ZIP and SHA-256 artifact upload.

Coverage is uploaded for inspection with no percentage gate. No test is
network-dependent. There are two test projects:

- `Jellyfin.Plugin.TrickplayCropper.UnitTests` for pure value and policy logic;
- `Jellyfin.Plugin.TrickplayCropper.ComponentTests` for the in-memory HTTP host,
  Jellyfin-interface mocks, real temporary filesystem, concurrency, cleanup,
  and native Skia fixtures.

The blocking scenario matrix includes:

- all interval, clamp, sprite/cell boundary, partial-final-sprite, invalid
  metadata, negative-position, and checked-overflow cases;
- canonical identity field order/format/hash/path/ETag and every included or
  deliberately excluded identity input;
- every HTTP status, default and alternate source, authorization-before-cache,
  200/304 headers, no cache-file header, no failed publication, cancellation,
  and one redacted request-level 500 log;
- public discovery types, singleton registration, task metadata, default 03:00
  trigger, and progress/cancellation forwarding;
- same-entry single-flight, different-entry concurrency, immutable HIT,
  disappearing HIT, all cancellation points, no-overwrite winner/loser,
  cleanup races, run boundary, fingerprint changes, file kinds, containment,
  reparse refusal, unknown files, cleanup serialization/cancellation,
  continuation after failure, counters, lock reclamation, lock order, and
  non-starvation;
- independently generated baseline and progressive JPEG grid fixtures covering
  non-origin and edge crops, output dimensions, decodability, and pixel
  agreement within compression tolerance;
- non-JPEG, truncated/corrupt, metadata mismatch, out-of-bounds, short-read, and
  destination-failure fixtures;
- cancellation before/waiting on the decode gate, between scanline batches, and
  before encode; four blocking encodes proving a fifth waits and can cancel;
- proof that the encoder leaves the cache-owned stream open and that failures
  leave no final or temporary file; and
- exact ZIP contents, parsed manifest identity/version/framework/ABI/artifacts,
  DLL framework and versions, and alignment of plugin/manifest constants.

Only the component-test project references
`SkiaSharp.NativeAssets.Linux.NoDependencies` 3.116.1 with
`PrivateAssets="all"`; it is non-packable and non-publishable. Do not reference
both native-assets variants. Concurrency tests use barriers and
`TaskCompletionSource`, not timing guesses. There is no benchmark threshold.

## 12. Release boundary and accepted risks

Release evidence is the successful workflow run, source commit, validated ZIP,
and SHA-256. A live Jellyfin 10.11.11 install/restart/decode/endpoint/cleanup
smoke may be run voluntarily but is not a release gate or required evidence.

The accepted first-deployment risks are that CI does not prove:

- real Jellyfin plugin load context;
- host-provided SkiaSharp managed/native resolution;
- runtime Controller, task, or registrator discovery;
- live authentication middleware;
- actual Jellyfin manager and media integration;
- Dashboard trigger persistence; or
- decode of a real Jellyfin Source Sprite.

The accepted resource risk is that trusted but abnormal Jellyfin metadata or
Source Sprites can exhaust managed or native resources, and four concurrent
decodes can amplify that pressure. There is no OOM recovery guarantee.

The accepted source-concurrency risk is that Jellyfin may replace a Source
Sprite while a request is reading it. v1 provides no cross-owner lock or retry
protocol for that race and relies on the next request's file snapshot to select
the current identity.

## 13. Decision traceability

This specification consolidates the resolutions of GitHub issues #4, #5, #6,
#7, #8, #9, #10, #11, and #13 under the Wayfinder map in #3. Issue #11
supersedes only issue #4's earlier mandatory real-server smoke statement. Issue
#13 deliberately moves inconsistent frame-count handling out of v1.
