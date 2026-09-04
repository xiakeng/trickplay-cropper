# Variant C — Guarantee

**Split by:** the promise being kept. One chapter per invariant the product
makes to the outside world. Every chapter has the same five sections — the
promise, the rule, what breaks without it, how it is enforced, and how a caller
can observe it — so the set reads as an audit rather than a narrative. The spine
is the invariant.

## File tree

```text
docs/business/
├── README.md
├── authorization-and-visibility.md
├── resolution-exactness.md
├── frame-determinism.md
├── probe-isolation.md
├── cache-identity-and-freshness.md
├── concurrency-safety.md
└── resource-bounds.md
```

## Chapters

### `README.md` — the guarantee map
The full list of promises in one table: the guarantee, the chapter that owns it,
the mechanism that enforces it, and the status code or header through which a
caller can observe it. Also states what the product deliberately does *not*
promise: no nearest-resolution substitute, no probe-implies-preview, no
prescribed client cache lifetime, no repair of Jellyfin-owned trickplay data.

- **Mermaid view:** one `flowchart TD` mapping each guarantee to its enforcing
  stage, so a reader can see which stage carries how many promises and where a
  change would break several at once.

### `authorization-and-visibility.md`
- **Promise:** a frame is delivered only to a caller who may play the video, and
  an Item the caller cannot see is indistinguishable from one that does not
  exist.
- **Rule:** authenticated caller resolved to a real user; logical-video playback
  authorization; user-scoped Source Video lookup; Media Source membership. No
  second Source Video playback check.
- **What breaks without it:** library-level hiding leaks; a caller could enumerate
  Items by response difference, or reach a version of a video they may not play.
- **Enforcement / observation:** `401` unauthenticated or API-key caller, `403`
  playback not permitted, `404` invisible or absent Item or unlisted Media Source.
- **Mermaid view:** one `flowchart TD` of the gates in order, each with its
  status. The ordering matters: the visibility gate precedes the playback gate so
  that an invisible Item never reveals why it was refused.

### `resolution-exactness.md`
- **Promise:** the width a caller receives is a width the server was configured
  to produce for that Media Source, exactly — never a substitute.
- **Rule:** take the minimum current Trickplay Resolution Target, normalize it
  with Jellyfin's rule for the selected Media Source to one Selected Trickplay
  Resolution, and require the generated metadata to match it exactly. No 320 px
  default, no alternate target, no nearest resolution.
- **What breaks without it:** a silent fallback serves a frame the server may
  never have generated, or one whose geometry the plugin then mis-crops.
- **Enforcement / observation:** `404` when no metadata matches the Selected
  Trickplay Resolution.
- **Mermaid view:** one `flowchart TD` from the target set to the single selected
  width, with every rejected alternative drawn as an explicit dead end. Showing
  the paths *not* taken is the whole content of this guarantee.

### `frame-determinism.md`
- **Promise:** the same playback position always selects the same frame, and no
  position is out of range.
- **Rule:** position to Frame Index by the generation interval, clamped to the
  last available frame; Frame Index to Source Sprite, cell, row, column, and crop
  rectangle by the generated tile geometry.
- **What breaks without it:** two clients scrubbing to the same position see
  different frames; a position past the last interval errors instead of resolving
  to the final frame.
- **Enforcement / observation:** `X-Trickplay-Frame-Index` on a probe, and the
  Frame Index component of the ETag on a preview.
- **Mermaid view:** one `flowchart LR` of the derivation chain plus a grid sketch
  of one Source Sprite with the selected cell marked.

### `probe-isolation.md`
- **Promise:** asking which frame a position selects never costs image work, and
  never promises that the frame can be delivered.
- **Rule:** the Trickplay Frame Probe reads configuration and metadata and
  computes the Frame Index; it does not resolve, stat, open, or snapshot a Source
  Sprite, and does not reach the Cache Tree or the encoder.
- **What breaks without it:** a client scrubbing across a timeline triggers sprite
  I/O and generation per position, turning a cheap question into the expensive
  answer; or a client trusts a probe and cannot explain the `404` that follows.
- **Enforcement / observation:** the probe's success body is empty and carries
  only `X-Trickplay-Frame-Index` and `Cache-Control: private, no-cache` — no
  ETag, so nothing about the image is disclosed, and no conditional behavior, so
  a probe cannot be cached as an image would be.
- **Mermaid view:** one `flowchart LR` of the shared pipeline truncated at the
  Frame Index, the remainder drawn detached and unreachable.

### `cache-identity-and-freshness.md`
- **Promise:** a cached preview is served only while it still belongs to the source
  version that produced it.
- **Rule:** Preview Cache Entry identity covers the media source, the frame
  geometry, the generation interval, the tile geometry, the thumbnail count, the
  Source Sprite, the sprite's version stamp derived from its length and
  modification time, the Frame Index, and the encoding quality. Any change moves
  the request to a different entry.
- **What breaks without it:** regenerated trickplay data is served stale, or a
  frame from one sprite version is delivered as if it belonged to another.
- **Enforcement / observation:** the ETag, and `X-Trickplay-Cache: HIT|MISS`
  telling the caller whether the bytes were generated or reused.
- **Mermaid view:** one `flowchart TD` of the identity inputs converging on the
  entry path and the ETag.

### `concurrency-safety.md`
- **Promise:** no caller reads a partially written entry, and two callers asking
  for the same frame pay for one generation.
- **Rule:** shared Cache Tree lease, then the entry lock for one Preview Cache
  Entry, then read or generate, then buffer the response before either lease is
  released. Write to a temporary entry, publish atomically, and treat losing the
  publication race as a hit. Exclusive tree lease only for orphan temporaries and
  directory pruning. A bounded number of decodes run at once.
- **What breaks without it:** torn JPEGs reach clients; duplicate generation
  multiplies decode cost under a scrub storm; a cleanup run deletes an entry a
  live request is mid-write on.
- **Enforcement / observation:** `X-Trickplay-Cache` and the `Server-Timing`
  stages, which expose where a request waited.
- **Mermaid view:** one `sequenceDiagram`, two concurrent callers and the Cache
  Tree, showing acquisition order, the race, and the loser reading the winner's
  entry.

### `resource-bounds.md`
- **Promise:** the Cache Tree does not grow without bound, and emptying it never
  disturbs a live request.
- **Rule:** a scheduled maintenance run takes a cutoff at its start, considers
  only entries last written before that cutoff, classifies each discovered file as
  an entry, an orphan temporary, or a skip, re-checks that the file is unchanged
  before deleting, and prunes empty directories last under an exclusive lease.
- **What breaks without it:** temporary storage fills; or a run deletes an entry
  that a request generated between discovery and deletion.
- **Enforcement / observation:** nothing caller-visible; this is the one guarantee
  a client cannot observe, and the chapter says so.
- **Mermaid view:** one `flowchart TD` of the run, cutoff first, classification in
  the middle, pruning last.

## What this variant is good at

- Auditable. A reviewer can answer "is every promise enforced, and where?" from
  the index alone, which is exactly what the final approval ticket needs.
- The uniform five-section shape makes an unenforced promise obvious: an empty
  "how it is enforced" is a defect in the product, not in the document.
- Deliberate non-promises get a home, which the other two variants have nowhere
  to put.

## What it is weak at

- **Nobody can tell you what happens, in order.** There is no narrative; a
  newcomer cannot follow one request through the set.
- The top-level lifecycle — the thing the ticket asks to be explained first —
  has no chapter and gets squeezed into the index.
- Implementation changes map badly: one new stage would touch four guarantee
  chapters, and one guarantee chapter can span four stages.
