# Trickplay Cropper Next-Stage Specification

- Status: Approved in GitHub issue #56
- Source map: GitHub issue #43
- Implementation tracker: GitHub issue #64
- Baseline: v1.0.0.0
- First automatic release: v1.0.1.0
- Plugin ID: `630fb758-9a29-4f2c-a54c-95793651bb8a`

## 1. Purpose and scope

This specification is the authoritative implementation contract for the next
stage of Trickplay Cropper. It extends the completed v1 server plugin with:

- source-specific adaptive selection from Jellyfin's current Trickplay
  Resolution Targets;
- a HEAD-based Trickplay Frame Probe on the existing Preview route;
- stable, structured Debug observability for cache and concurrency behavior;
- a three-layer Business Documentation set;
- human-gated GitHub Release and Jellyfin repository-manifest automation; and
- a manually invoked, no-mock Integration Harness for the target local Jellyfin
  host.

The next stage preserves the v1 compatibility, Source Sprite, image-processing,
Preview Cache Entry, Cache Tree, cleanup, and package contracts except where
this document explicitly changes resolution selection, assigns playback policy
solely to the authorized logical video, adds HEAD, or adds observability.

The fixed delivery order is:

1. implement and verify business logic;
2. write the Business Documentation set and update the repository README; and
3. implement and run the manual local Integration Harness.

Release automation may be implemented in the same issue, but documentation
must describe implemented behavior and must exist before the live suite runs.

## 2. Compatibility and preserved v1 contracts

The production plugin continues to target Jellyfin Server 10.11.11, the
10.11.0.0 plugin ABI, and `net9.0`. The stable plugin name, GUID, assembly name,
route, JPRM packaging shape, and Jellyfin discovery contracts do not change.

The following v1 decisions remain authoritative:

- ADR 0001 governs process-local entry locks, Cache Tree leases, cleanup
  coordination, containment, and atomic no-overwrite publication.
- ADR 0002 governs trust in Jellyfin-owned Source Sprites, bounded concurrent
  decode, scanline cropping, and the accepted source-mutation and resource
  risks.
- GET continues to return the existing JPEG representation, ETag, conditional
  request behavior, cache disposition, content disposition, and timing data.
- Preview Cache Entries remain derived, server-local, and safe to discard under
  the existing cleanup policy.

The current `main` baseline is not the behavior described by the remainder of
this document: it exposes GET only and selects a fixed 320-pixel metadata entry.
The next-stage implementation replaces that fixed selection with the exact
source-specific policy below.

## 3. Shared Preview context

GET and the Trickplay Frame Probe share one request-scoped Preview-context
pipeline. The pipeline ends after Frame Index calculation and returns a closed,
typed result consumed by both operations.

The shared pipeline owns, in order:

1. request parsing and non-negative position validation;
2. current-user resolution;
3. user-scoped logical-video lookup;
4. logical-video playback authorization;
5. Media Source membership validation;
6. user-scoped Source Video lookup;
7. one current Trickplay configuration snapshot;
8. Trickplay Resolution Target selection and source-specific normalization;
9. one generated-resolution metadata lookup;
10. exact metadata validation; and
11. Frame Index calculation and end clamping.

### 3.1 Request values

The existing route remains:

```http
/TrickplayCropper/Videos/{ItemId}/Preview
```

The effective request values are:

| Value | Rule |
| --- | --- |
| `ItemId` | Required GUID identifying the logical video |
| `MediaSourceId` | Optional GUID; defaults to `ItemId` |
| `PositionTicks` | Required 64-bit Jellyfin tick value; must be non-negative |

There is no `Width` parameter. The caller cannot select a target or generated
width.

GET keeps typed ASP.NET binding. HEAD uses nullable raw strings without
required-binding metadata, then parses at the action or probe boundary. The two
operations nevertheless produce the same effective query and validation
outcomes.

### 3.2 Authorization and visibility

The authenticated current user must be resolved before shared Preview work.
The pipeline performs a user-scoped lookup of the logical video and checks its
playback access before any cache or Source Sprite work.

The requested Media Source must be a member enumerated by the authorized
logical video. The effective Source Video lookup remains user-scoped so an
invisible item cannot be used indirectly.

The implementation must not perform a second playback-policy decision on the
selected Source Video. The authorized logical video and its enumerated Media
Source membership own playback policy; the Source Video lookup owns visibility.
This avoids contradictory authorization decisions without weakening source
membership.

This ownership rule deliberately changes the v1 authorization contract, which
required playback access independently on both the logical and selected Source
Videos. It does not remove the logical-video playback decision, Media Source
membership proof, or user-scoped Source Video visibility lookup.

An API key without a current Jellyfin user is not user-scoped playback
authority and is forbidden.

## 4. Selected Trickplay Resolution

### 4.1 Configuration snapshot

Each request reads Jellyfin's current `WidthResolutions` once. It does not retry
or combine values across configuration revisions.

- A null or unreadable array is an internal error.
- An empty array has no current Trickplay Resolution Target.
- Any non-positive element makes the configuration structurally invalid.
- Positive duplicates are valid.
- Array ordering is irrelevant.

The chosen Trickplay Resolution Target is the minimum positive value in the
snapshot.

### 4.2 Source-specific normalization

Normalize the chosen target using the matched playback Media Source as the sole
authority:

1. If the matched Media Source's Video Stream width exists and is smaller than
   the target, clamp to that source width.
2. If the source width is null, do not clamp.
3. Normalize the result to an even width using Jellyfin's rule.
4. Treat a non-positive normalized result or checked-arithmetic failure as an
   internal error.

The resulting even width is the Selected Trickplay Resolution. Different raw
targets that normalize to the same selected value identify the same
representation.

### 4.3 Exact metadata selection

Query generated Trickplay metadata once for the authorized effective Source
Video GUID. Require a dictionary key exactly equal to the Selected Trickplay
Resolution.

Do not try:

- the v1 fixed width of 320;
- another current Trickplay Resolution Target;
- an unselected generated key;
- a nearest, larger, or smaller generated width;
- the logical video's width;
- another Media Source; or
- a reconstructed Jellyfin storage path.

Both the metadata dictionary key and the metadata `Width` must equal the
Selected Trickplay Resolution. No metadata, no exact key, or no thumbnails is
unavailable content. Non-positive `Height`, `Interval`, `TileWidth`, or
`TileHeight`, contradictory selected metadata, and checked-arithmetic failure
are internal errors. `Bandwidth` and all unselected entries are ignored.

One request uses one configuration snapshot and one generated-resolution
dictionary read. A concurrent configuration or generation change may therefore
produce an ordinary not-found or internal-error outcome; the next request reads
current state.

### 4.4 Frame Index

Calculate the zero-based Frame Index from `PositionTicks` and the selected
metadata interval using checked arithmetic. Clamp a position at or beyond the
generated sequence to the final available Frame Index.

GET and the Trickplay Frame Probe must produce the same Frame Index for the same
authorized request context.

## 5. Outcome mapping

Map the shared and GET-only outcomes as follows:

| Status | Meaning |
| --- | --- |
| `400 Bad Request` | Missing or malformed request values, or negative `PositionTicks` |
| `401 Unauthorized` | Authentication cannot establish a usable session |
| `403 Forbidden` | API key has no current user, or logical-video playback is denied |
| `404 Not Found` | Concealed or unavailable item, non-member source, no current target, no exact metadata, no thumbnails, or GET-only Source Sprite absence |
| `500 Internal Server Error` | Invalid configuration, contradictory metadata, arithmetic failure, operational failure, cache-safety failure, or encode failure |

Unavailable and concealed cases must not expose internal distinctions to the
client. Pass the request cancellation token to asynchronous Jellyfin manager
calls. The shared pipeline and Trickplay Frame Probe add no explicit
cancellation checkpoints, do not convert cancellation into a closed probe
outcome, and do not log cancellation as a probe failure. Cancellation follows
the host's existing behavior rather than becoming a successful or cacheable
outcome.

## 6. GET Preview contract

After the shared pipeline succeeds, GET alone:

1. resolves and snapshots the Jellyfin-owned Source Sprite through Jellyfin's
   Trickplay manager;
2. validates codec-time dimensions and crop bounds;
3. creates Preview Identity and ETag;
4. evaluates `If-None-Match`;
5. acquires Cache Tree and Preview Cache Entry coordination as needed; and
6. crops and encodes a JPEG on a miss.

GET retains the v1 success contract:

- `200 OK` with a JPEG body for generated and cached representations;
- `304 Not Modified` for a matching conditional request;
- ETag behavior tied to the actual Preview Identity;
- `Cache-Control`, `Content-Disposition`, `X-Trickplay-Cache`, and
  `Server-Timing`; and
- no header exposing a cache-file path.

The Selected Trickplay Resolution becomes the width in Preview Identity. The
raw Trickplay Resolution Target is not part of identity. The Cache Tree remains
the compatible `preview-v1` namespace; there is no migration or proactive
deletion when configuration changes. Unselected compatible entries age out
through normal cleanup.

## 7. Trickplay Frame Probe contract

Add HTTP HEAD on the existing Preview route as the Trickplay Frame Probe. A
dedicated `ITrickplayFrameProbe` accepts a normalized Preview query, the current
claims principal, and cancellation token. It returns only:

- success carrying Frame Index;
- BadRequest;
- Unauthorized;
- Forbidden;
- NotFound; or
- InternalError.

It has no NotModified outcome.

On success, HEAD returns `200 OK`, an empty body, and exactly these two
plugin-owned headers:

```http
X-Trickplay-Frame-Index: <zero-based index>
Cache-Control: private, no-cache
```

The plugin must not add ETag, `Server-Timing`, `X-Trickplay-Cache`,
`Content-Length`, or content type to the HEAD response. Middleware or server
headers outside plugin ownership may still appear on the wire.

Every HEAD outcome is bodyless, including failures. `If-None-Match` is ignored.
Successful HEAD proves authorization, exact metadata selection, and Frame Index
calculation only. It does not prove that a Source Sprite exists or that GET can
decode, cache, or encode the representation.

The Trickplay Frame Probe must not resolve or inspect a Source Sprite, create
Preview Identity, evaluate conditional GET, access the Cache Tree, acquire a
decode permit, snapshot the filesystem, calculate sprite/cell/row/column/crop
geometry, take a lock, write state, retry, or invoke the encoder. The shared
pipeline also has no dependency on these GET-only facilities. GET alone computes
sprite index, cell, row, column, and crop geometry after Source Sprite
resolution.

Unsupported methods advertise `Allow: GET, HEAD`.

## 8. Diagnostics and Debug observability

Expected resolution-unavailable outcomes are logged at Debug with the stable
reason values `NoConfiguredTarget`, `NoGeneratedMetadata`,
`SelectedResolutionMissing`, `NoThumbnails`, and `SourceSpriteUnavailable`.
Do not add plugin logs for ordinary `400`, `401`, `403`, or concealment
outcomes. Internal failures include all known redaction-safe request values,
configured targets, chosen target, Selected Trickplay Resolution,
normalization source width, generated keys, selected metadata, Frame Index,
crop values, and source fingerprint needed to reconstruct the failure.

Logs must exclude:

- access tokens, authorization headers, and claims;
- user names and media titles;
- media and Source Sprite paths; and
- Cache Tree paths.

Add a stable structured Debug protocol identified by EventId and EventName for:

- cache disposition;
- Preview Cache Entry lock wait and ownership;
- Cache Tree lease wait;
- decode-permit wait;
- Frame Index; and
- sprite index.

These events are an explicit product-side seam for the manual live suite. They
must be Debug-only, redaction-safe, deterministic in identity and fields, and
behavior-neutral. They must not add production control flow or make contention
timing a correctness condition. When the host operates at Information level,
it pays no field-construction or other logging cost for the smoke-suite
protocol.

ADR 0003 records why HTTP and filesystem evidence alone are insufficient for
the Scrub Storm's coordination questions, and why stable structured events are
used instead of parsing free-form messages.

## 9. Business Documentation contract

Create exactly 25 files under `docs/business/`, read in the order participants,
lifecycle, then design. The root `README.md` owns that reading path and a
route-by-question table.

### 9.1 Participants layer

`docs/business/participants/` owns responsibility and boundaries. It never
contains mechanism, ordering, or status rules.

It contains seven files:

- `README.md` for the ownership map;
- `client.md`;
- `jellyfin-server.md`;
- `frame-probe.md`;
- `preview-request.md`;
- `cache-tree.md`; and
- `cleanup-task.md`.

The client conversation sequence belongs in this layer because it describes an
edge between parties.

### 9.2 Lifecycle layer

`docs/business/lifecycle/` owns mechanism and order. It never owns rationale,
rejected alternatives, or what-breaks catalogues.

It contains nine files:

- `README.md` for the complete lifecycle;
- `source-resolution.md`;
- `frame-probe.md`;
- `frame-selection.md`;
- `preview-generation.md`;
- `preview-cache.md`;
- `cache-coordination.md`;
- `response-contract.md`; and
- `scheduled-cleanup.md`.

Every Lifecycle chapter ends with a short anchor naming the carrying types and
methods. Anchors use names, never line numbers.

### 9.3 Design layer

`docs/business/design/` owns promises, consequences, rationale, rejected
alternatives, and deliberate non-promises. It links to lifecycle mechanism
instead of restating it.

It contains eight files:

- `README.md` for the guarantee map and non-promises;
- `authorization-and-visibility.md`;
- `resolution-exactness.md`;
- `frame-determinism.md`;
- `probe-isolation.md`;
- `cache-identity-and-freshness.md`;
- `concurrency-safety.md`; and
- `resource-bounds.md`.

Decode-permit rationale belongs in `resource-bounds.md`, which covers host CPU
as well as disk.

### 9.4 Deduplication and diagrams

Each layer has one job and no rule is stated twice. A chapter links to the layer
that owns a fact rather than copying it. Participants and Design carry no code
anchors.

Mermaid diagrams use these standing constraints:

- top-down by default;
- left-to-right only for a short chain that fits the reading column;
- about four nodes at most in one rank;
- about eight ranks at most in one view;
- split oversized views; and
- never share one terminal node across ranks.

The repository README is updated after implementation with the project
introduction, features, installation, update and exact-version rollback,
build/test, and manual Integration Harness instructions. The harness guidance
discloses credentials and credential risk, privileges, mutations, both
Privileged Phases, the Restart Budget, the Retained End State, and every live
coverage gap. It does not link the legacy `docs/spec/` area and does not create
a separate development-docs tree.

## 10. Automated Release and manifest contract

### 10.1 Release preparation

Every push to `main`, including documentation-, test-, tooling-, and
Actions-only changes, opens or updates one fixed-branch pull request titled
`auto-release new version`. An ordinary push never publishes a release.

The Release Pull Request is authored with `GITHUB_TOKEN`. Its body contains the
changelog from the previous Release tag to current `HEAD`, or from the root
commit before the first Release, and the computed next four-component version.
Its diff changes only the plugin build manifest's version and changelog.

The merged build manifest is the single version source. Routine releases
increment the third component. The committed but unpublished `1.0.0.0` is the
floor, so the first automatic Release is `1.0.1.0`. A maintainer may edit major
or minor components in the Release Pull Request.

The workflows fail closed before mutation or publication if the required
`main` branch protection, repository workflow permissions, bot review
capability, or `RELEASE_BOT_PAT` secret is absent or incompatible.

Publication requires a human to approve and merge the Release Pull Request
using an allowed non-merge-commit method.

### 10.2 Publication

Publication runs only when an internal, same-repository pull request with the
exact Release Pull Request title closes as merged. Before publishing, it runs
the existing locked restore, formatting, Release build, unit, component, JPRM,
and package-validation gates.

Publish the sole JPRM artifact as a stable GitHub Release:

- tag `v<version>`;
- title `Trickplay Cropper <version>`;
- body equal to the approved changelog; and
- one installable JPRM ZIP.

Once JPRM produces the package, perform no additional post-build identity
attestation. Retry logic is minimally idempotent: reuse an existing Release or
same-named asset instead of creating duplicates. Generated package outputs are
not committed.

### 10.3 Jellyfin repository manifest

After publishing the Release, build a complete manifest entry from the stable
plugin GUID, build-manifest identity, actual ZIP version, changelog, minimum
target ABI, immutable versioned download URL, case-insensitive MD5 checksum,
and embedded metadata UTC timestamp.

The first Release creates the repository-root manifest skeleton. Keep all
published stable entries in descending numeric-version order. Exclude drafts,
prereleases, failed builds, and Releases without the required asset.

Submit the manifest change in a second pull request authored using the
`RELEASE_BOT_PAT` identity so required checks run without manual workflow
approval. After the final push and successful checks, use `GITHUB_TOKEN` as a
distinct actor to approve and merge it. Do not configure a ruleset bypass actor
and do not use the PAT for the merge.

Rely on the non-recursive behavior of `GITHUB_TOKEN` so that the bot-merged
manifest change does not open another Release Pull Request.

The contribution workflow in `AGENTS.md` continues to govern implementation of
this contract and every ordinary human- or agent-authored change. The Release
and Manifest Pull Request actors and merge gates above describe the runtime
automation approved in #56; they do not authorize an agent to merge this
implementation pull request, bypass a ruleset, or alter the contribution
workflow for unrelated changes.

## 11. Manual local Integration Harness

### 11.1 Project and human input

Add a dedicated `net9.0` Integration Harness console project to the solution
with a committed locked dependency graph. It is invoked manually with
`dotnet run`; default CI never executes it.

A human-authored, gitignored repository-root `harness.json` sits beside a
committed example and contains only:

- one administrator user access token;
- exactly two playable Item IDs; and
- one Item ID that exists but is invisible to that user.

The local default HTTP Jellyfin endpoint, Debug build location, deterministic
seed, request volume, and timeouts are source constants. The harness performs
no automatic media discovery and never prints the token. It validates that the
human-supplied subjects satisfy the required playable and invisible roles, or
fails before any host mutation.

### 11.2 Deployment and privilege boundary

Build the plugin in Debug and deploy only its DLL and PDB. The live run does not
use JPRM, ZIP metadata, package validation, or `deps.json`.

Delete installed plugin directories by stable plugin GUID and rely on
Jellyfin's verified name/version directory fallback to regenerate host-owned
plugin metadata after deployment.

Split execution into an unprivileged driver for HTTP, assertions, parsing, and
output, plus a small privileged host operation for plugin/cache filesystem
work, logging configuration, and systemd restart. Invoke `sudo` at exactly two
Privileged Phase boundaries and perform exactly two service restarts.

Do not use `sudo -n`, edit sudoers, store elevation credentials, add a separate
run lock, add a server-version gate, or add a confirmation prompt.

Before mutation, refuse to start if the sibling `logging.json.bak` Logging
Snapshot already exists. This is the only pre-mutation unexpected-state guard
and also prevents a concurrent second run. Recovery after interruption requires
human inspection and removal of the surviving snapshot before a new run.

Privileged Phase 1:

1. delete prior installed plugin directories matching the stable plugin GUID;
2. empty only the plugin Cache Tree;
3. deploy the Debug DLL and PDB as `jellyfin:jellyfin`, with directory mode
   `0755` and file mode `0644`;
4. copy the current logging configuration to the Logging Snapshot while
   preserving metadata;
5. add only the plugin parent category to
   `Serilog.MinimumLevel.Override`, leaving the default level and sinks intact;
   and
6. restart Jellyfin.

After restart 1, require health, plugin Active status, and at least one real
plugin Debug event as the Health, Load-Proof, and Debug-Proof Gates. A gate
failure skips smoke cases but still enters unconditional restoration.

### 11.3 Fixed smoke cases

Run exactly four live cases:

1. an invented invalid token;
2. an authenticated request for the invisible Item;
3. start and beyond-end positions for both playable Items; and
4. the two-client Scrub Storm.

HEAD and GET share the first three cases where applicable. For playback
boundaries, read Jellyfin Trickplay metadata independently and predict Frame
Index without using plugin output. Require HEAD's exact status, headers, and
empty body. Require GET's JPEG contract, cache headers, ETag Frame Index
component, and repeatability.

The invented invalid token must produce `401` for HEAD and GET, with an empty
HEAD body. The authenticated invisible Item must produce concealed `404`
responses for both methods. Start and beyond-end requests must agree with the
independently calculated Frame Index.

The Scrub Storm uses:

- seed `0x5EEDC0DE`;
- two logical clients;
- three barrier-controlled lanes per client;
- twelve positions per lane per playable Item;
- random-jump, large-range fast-sweep, and small-range precise-drag
  trajectories;
- two rounds per shape;
- a ten-second per-request timeout; and
- a thirty-second quiescence timeout.

Fan out HEAD for each round before GET. Arrange at least five distinct Preview
identities and one deterministic repeated identity.

Hard Scrub Storm pass conditions are observable HTTP, representation, ETag,
and filesystem behavior:

- every request succeeds without timeout or `500`;
- HEAD stays bodyless and agrees on Frame Index;
- repeated GET bytes and ETags are stable;
- at least one MISS-to-HIT transition occurs;
- after quiescence, exactly one canonical JPEG exists for each distinct Media
  Source and Frame Index pair; and
- no temporary publication residue remains.

Parse only the newest Jellyfin log and only stable structured plugin Debug
events. Reconcile request disposition, Frame Index, and sprite index when
present. Entry-lock, Cache Tree lease, and decode-permit waits are reported as
`observed` or `not-observed` diagnostics. They are not live pass gates;
deterministic ordering and cancellation remain component-test obligations.

### 11.4 Restoration and retained state

Privileged Phase 2 always:

1. restores the logging configuration byte-for-byte from the Logging Snapshot;
2. deletes the snapshot;
3. restarts Jellyfin; and
4. re-establishes health.

Exit zero requires all hard smoke conditions, successful restoration, and
post-restart health.

The harness writes no evidence, transcript, or state files. It may mutate only
matching plugin installation directories, the plugin Cache Tree, the Logging
Snapshot and override, and Jellyfin service state. It never logs in, provisions
credentials, triggers cleanup, changes server/library configuration, changes
libraries or metadata, regenerates Trickplay, writes Source Sprites, or modifies
Jellyfin logs.

The Retained End State keeps the Debug plugin installation and populated Cache
Tree while restoring logging. On assertion failure, keep the diagnostic scene
while still attempting restoration.

## 12. Verification contract

Good automated tests assert visible policy through closed outcomes, HTTP
responses, headers, bytes, filesystem state, structured events, repository
artifacts, and workflow state. They do not pin private helper structure,
incidental call counts, free-form messages, or timing luck.

### 12.1 Business logic and HTTP

Extend the existing in-memory ASP.NET component seam for GET and the shared
Preview-context pipeline. Add focused unit matrices for:

- request parsing and status mapping;
- current-user and logical-video authorization;
- Media Source membership and source visibility;
- configuration shape and minimum-target selection;
- duplicate and order independence;
- even normalization, source-width clamping, and null source width;
- exact metadata selection and validation;
- Frame Index boundaries and checked arithmetic;
- cancellation; and
- complete redacted diagnostics.

Add focused Trickplay Frame Probe tests for every closed outcome and Frame Index
clamping. Extend HTTP component tests for nullable raw HEAD binding, missing and
malformed inputs, every status, exact present and absent headers, ignored
`If-None-Match`, and `405` with `Allow: GET, HEAD`.

Test the public Trickplay Frame Probe contract rather than internal shared
pipeline stage methods.

Add one real-Kestrel automated seam that proves an empty HEAD body for every
success and failure status. TestServer alone is insufficient for transport-level
HEAD body suppression.

Do not add a behavioral spy test merely to prove that the Trickplay Frame Probe
did not invoke Source Sprite, Cache Tree, or encoder collaborators. Enforce that
guarantee structurally through dependency direction and review.

Preserve and extend GET coverage for generated, cached, and conditional
responses; default and alternate Media Sources; authorization before cache
access; concealment; invalid metadata; Source Sprite failures; cancellation;
and a complete redacted `500` diagnostic.

### 12.2 Identity, cache, and native image work

Preserve the existing Frame Selection and Preview Identity unit seams. Assert
that Selected Trickplay Resolution is the identity width and that distinct raw
targets normalizing to identical content do not fragment identity.

Preserve real-filesystem Cache Tree component coverage for same-entry
single-flight, different-entry concurrency, immutable response buffering,
atomic no-overwrite publication, cancellation, cleanup races, containment,
reparse refusal, and lock reclamation. Use barriers and task completions rather
than sleeps or duration thresholds.

Preserve the native Skia component seam for JPEG subset scanline decoding,
source geometry, cancellation, four-permit gating, encoding, and failure
cleanup. Add stable Debug protocol checks at the module boundary without
asserting free-form messages.

Keep Cache Tree lease fairness, Preview Cache Entry lock ordering,
decode-permit waiting and cancellation, cleanup interaction, and deterministic
same-entry overlap as explicit component-test obligations. The live harness may
observe them but does not replace deterministic automated coverage.

### 12.3 Release, documentation, CI, and live verification

Extend release-contract and Package Validator tests for four-component version
alignment, the single version source, first-release floor, exact JPRM artifact,
manifest shape/order/history, actual ZIP MD5 and embedded timestamp, generated
artifact exclusion, workflow trigger and permission guards, and the two-actor
pull-request flow.

Keep default GitHub Actions on pull requests and pushes to `main`: locked
restore, formatting, Release build, unit tests, component tests, JPRM packaging,
package validation, and inspectable coverage. Actions remain pinned. The local
Integration Harness is never added to default CI.

Validate all 25 Business Documentation files, every relative link, and every
Mermaid parse/render. Review rendered dimensions against the approved sizing
constraints. GitHub's own Mermaid renderer remains a human visual check.

The manual Integration Harness is the highest end-to-end seam. It uses real
Kestrel, Jellyfin authentication and managers, generated metadata and Source
Sprites, the real Cache Tree, real Debug logs, and the real plugin load boundary
without mocks.

The live suite does not prove installation of the shippable ZIP because it
deploys a Debug DLL and PDB. CI Package Validator and the Release workflow own
that contract.

Keep the following live gaps explicit and cover their policy through automated
unit or component tests where practical: playback-policy `403`, alternate Media
Sources, every `500` shape, clamp-to-source-width normalization, selection among
multiple targets, media-side Source Sprite storage, no-thumbnails, missing
manager path or Source Sprite, cleanup behavior, Cache Tree seeding, and Cache
Tree lease contention.

The first production Manifest Pull Request remains the real-world verification
that `GITHUB_TOKEN` can approve and merge the PAT-authored pull request after all
protections pass.

## 13. Out of scope and explicit non-promises

The seven deliberate product non-promises are:

- no nearest-resolution substitute;
- no promise that successful Trickplay Frame Probe means GET will succeed;
- no plugin-prescribed client cache lifetime;
- no repair of Jellyfin-owned Trickplay data;
- no persistence promise for derived Cache Tree content;
- no detection of a Source Sprite replaced during a request; and
- no content negotiation for type, dimensions, or quality.

The next stage does not include:

- client implementation, cache keys, expiry, invalidation, or scheduling;
- a `Width` parameter, user-selected resolution, nearest-resolution behavior,
  fixed-320 fallback, alternate-target fallback, or cross-source fallback;
- a promise that successful HEAD implies successful GET;
- generation, repair, or completeness validation of Jellyfin-owned Trickplay
  data beyond selected metadata and GET Source Sprite checks;
- a new Cache Tree namespace, migration, proactive configuration-change
  deletion, full-Sprite bitmap cache, or cross-process coordination;
- Source Sprite replacement detection during a request;
- support beyond Jellyfin Server 10.11.11, remote targets, container topology
  discovery, or a broader operating-system matrix;
- CI execution of the live suite, media discovery, user/credential provisioning,
  or a real-library coverage matrix;
- live cleanup, Cache Tree seeding, alternate-source, playback-denial, synthetic
  `500`, or lease-contention verification;
- publication in the official Jellyfin plugin catalog;
- an automated withdrawal or rollback operation, withdrawal ledger, release
  queue, version pre-emption, public-download attestation gate, post-build
  identity attestation, or retry evidence-adoption state machine;
- automatic publication from an ordinary `main` push;
- content negotiation, alternate formats, placeholders, full-decode fallback,
  or JPEG quality and crop changes;
- a separate development-documentation tree;
- guaranteed recovery after `SIGKILL`, power loss, lost restoration privilege,
  or a failed second restart; or
- performance thresholds or a requirement that live contention waits occur.

Published stable manifest history supports user-selected exact-version rollback;
no rollback workflow mutates Releases or manifest history.

## 14. Decision traceability

This specification consolidates the completed Wayfinder map #43 and approved
decisions #44 through #56. Issue #56 records the user's final approval and that
no additional product or architecture choice is required before implementation.
Issue #64 is the implementation tracker.

The complete three-layer Business Documentation prototype is retained at commit
`81646ce` as a structural source, not production documentation. The rejected
release-state-machine prototype at `d52cc0f` is evidence for alternatives and
must not be implemented as the chosen design.

ADR 0001 and ADR 0002 remain authoritative. ADR 0003 records the new, approved
observability tradeoff. Integration Harness terms such as Privileged Phase,
Restart Budget, Load-Proof Gate, Logging Snapshot, Retained End State,
Debug-Proof Gate, and Scrub Storm deliberately remain outside `CONTEXT.md`; the
domain glossary continues to contain product vocabulary only.
