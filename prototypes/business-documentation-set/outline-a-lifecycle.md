# Variant A — Lifecycle

**Split by:** the order a request flows through the plugin. One chapter per
stage, read top to bottom. The spine is time.

**Fully drafted under [`draft/`](draft/README.md).**

## File tree

```text
docs/business/
├── README.md
├── source-resolution.md
├── frame-probe.md
├── frame-selection.md
├── preview-generation.md
├── preview-cache.md
├── cache-coordination.md
├── client-interaction.md
└── scheduled-cleanup.md
```

## Chapters

### `README.md` — the whole lifecycle on one page
Two entry points into one shared pipeline: the Trickplay Frame Probe answers
"which frame is this position?" without touching an image; the Trickplay Preview
request answers "give me that frame" and may generate it. Names the stages,
links the chapters in flow order, and states the one rule the whole product rests
on: Trickplay Cropper consumes Jellyfin-owned trickplay data and never generates
or repairs it.

- **Guarantees stated here:** none; this is the index.
- **Mermaid view:** one `flowchart TD`, both entry points, every stage as a node,
  failure exits drawn to the side. The only diagram in the set that a reader can
  use as a map of everything else.

### `source-resolution.md` — from configured targets to one exact resolution
What the plugin asks the server for, and how a raw request becomes a single
usable width. Takes the minimum current Trickplay Resolution Target, normalizes
it with Jellyfin's rule for the selected Media Source, and demands that the
generated trickplay metadata match the Selected Trickplay Resolution exactly.
Also owns the three authorization gates every request passes before any image
work: logical-video playback authorization, Media Source membership, and a
user-scoped Source Video lookup — and the deliberate absence of a second Source
Video playback check.

- **Guarantees:** no substitute resolution is ever served; an Item the caller
  cannot see does not exist.
- **Mermaid view:** two shallow `flowchart TD` views rather than one deep one —
  the gates in the order they must run, then the selection and its normalization.
  Rejections collapse to one exit per view because the two tables already carry
  the exact status for each; what prose cannot show is that the gate order is not
  rearrangeable and that the selection admits no alternative branch.

### `frame-probe.md` — the Trickplay Frame Probe
The HEAD operation: what it is allowed to read, what it is forbidden to touch,
and what it returns. Computes a Frame Index from configuration and metadata
alone, without resolving, statting, opening, or snapshotting a Source Sprite,
and without reaching the Cache Tree or the encoder. Successful probe returns only
`X-Trickplay-Frame-Index` and `Cache-Control: private, no-cache` — no ETag, no
conditional behavior — and is explicitly *not* proof that a following preview
request can resolve a Source Sprite.

- **Guarantees:** a probe never costs image work; a probe answer never promises a
  preview.
- **Mermaid view:** one `flowchart TD` of the shared pipeline truncated at the
  Frame Index, with the forbidden remainder drawn as a detached, unreachable
  subgraph. The diagram's value is showing what is *not* reachable.

### `frame-selection.md` — Frame Index and sprite geometry
The arithmetic, and why it is total: playback position to Frame Index, clamped to
the last available frame; Frame Index to Source Sprite, cell, row, column, and
the crop rectangle inside that sprite. Explains why clamping is a business rule
(the last partial interval still has a frame) rather than an error.

- **Guarantees:** the same position always selects the same frame; no position is
  out of range.
- **Mermaid view:** one `flowchart TD` of the derivation chain, plus a small grid
  sketch showing one Source Sprite as cells with the selected cell highlighted. The
  grid is the only way to make row/column/crop offsets obvious.

### `preview-generation.md` — cropping one Trickplay Preview
How a single frame comes out of a Source Sprite: horizontal-subset scanline
decode of only the selected columns, row skipping to the crop, JPEG encode at a
fixed quality. Explains the business reason for subset decoding — a Source Sprite
is far larger than one frame, and decoding all of it would make a preview cost
more than the frame is worth — and why a bounded number of decodes may run at
once.

- **Guarantees:** generation cost stays proportional to one frame; a malformed
  Source Sprite fails the request rather than producing a wrong image.
- **Mermaid view:** one `flowchart TD` from Source Sprite path to buffered JPEG,
  with the validation gates and the decode-permit wait marked.

### `preview-cache.md` — Preview Cache Entry identity and the Cache Tree
What makes two previews "the same": the identity inputs, the source version stamp
derived from the sprite's length and modification time, and the resulting ETag.
Then the Cache Tree layout under Jellyfin's temporary storage and the `preview-v1`
namespace, and what a cache hit versus a miss means to the caller.

- **Guarantees:** an entry is served only while it matches the source version
  that produced it; regenerated trickplay data cannot be served stale.
- **Mermaid view:** one `flowchart TD` with the identity inputs grouped into four
  labelled clusters by the question each answers, converging on the digest that
  yields the entry path and the ETag. Convergence is what prose describes badly,
  and grouping is what keeps the view inside the reading column.

### `cache-coordination.md` — leases, entry locks, and the publication race
The ordering rules that let concurrent requests share one Cache Tree: a shared
Cache Tree lease, then an entry lock for one Preview Cache Entry, then the read
or generation, then the response buffered *before* either lease is released.
Covers the write-to-temporary-then-publish-atomically race and why losing it is a
hit rather than an error, and notes where exclusive tree leases are required.

- **Guarantees:** no caller ever reads a partially written entry; two callers
  generating the same frame cost one generation.
- **Mermaid view:** one `sequenceDiagram` with two concurrent callers and the
  cache, showing the acquisition order, the race, and the loser reading the
  winner's entry. Ordering between parties is exactly what a sequence diagram is
  for; a flowchart would hide it.

### `client-interaction.md` — the client's side of the conversation
The HEAD/cache/GET interaction as the client experiences it, and the response
headers that carry the contract: `X-Trickplay-Frame-Index`, `ETag`,
`X-Trickplay-Cache`, `Server-Timing`, `Cache-Control`. States plainly that the
cache check before requesting a preview is **client-owned policy**: the plugin
supplies identity and freshness, and prescribes no key, expiry, or invalidation
rule.

- **Guarantees:** the client can always tell whether it may reuse what it holds;
  the plugin never dictates client cache lifetime.
- **Mermaid view:** one `sequenceDiagram`, client and plugin, scrub to probe to
  conditional request to `200` or `304`. This is the variant-B participant view,
  kept as a single chapter because only the client boundary needs it.

### `scheduled-cleanup.md` — emptying the Cache Tree
The maintenance run: when it happens, the cutoff that makes an in-flight entry
untouchable, how a discovered file is classified as an entry, an orphan
temporary, or something to skip, why deletion re-checks that the file is
unchanged, and how empty directories are pruned last under an exclusive lease.

- **Guarantees:** cleanup never deletes an entry a live request is using; the
  Cache Tree does not grow without bound.
- **Mermaid view:** one `flowchart TD` of the run, cutoff first, classification
  in the middle, pruning last, with the exclusive-lease branches marked.

## What this variant is good at

- One request can be followed end to end without jumping files.
- Chapter boundaries match the pipeline, so an implementation change lands in
  exactly one chapter.
- The index doubles as an onboarding read.

## What it is weak at

- Cross-cutting rules (cache identity, the authorization gates) are tempting to
  repeat in several chapters. Mitigation: each rule has one owning chapter and
  the others link to it.
- A reviewer cannot check "is every promise enforced?" without reading all nine
  files. Mitigation: each chapter opens with the guarantees it upholds.
