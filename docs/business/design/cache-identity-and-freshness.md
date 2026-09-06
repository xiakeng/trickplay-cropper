# Cache identity and freshness

## The promise

A cached preview is served only while it still belongs to the Source Sprite version that
produced it. Reused source-fact and generated-metadata observations are served only within
their separate explicit absolute lifetimes.

## What breaks without it

- **Regenerated trickplay data is served stale.** Jellyfin may regenerate at any time.
  A cache keyed only by position and item would keep serving frames from the previous
  generation indefinitely, and the client would have no way to notice — the response
  looks identical.
- **A frame from one sprite version is delivered as another's.** Same coordinates,
  different pixels. The response is a valid JPEG of the wrong image, which is the
  silent failure the whole design avoids elsewhere too.
- **Geometry changes are masked.** If the tile geometry or interval changed on
  regeneration, a stale entry would encode a crop that no longer corresponds to any
  frame of the current data.
- **Two artifacts collide.** Different encoding qualities, or different Media Sources
  of the same video, would share one entry if identity did not cover them.
- **Continuous probing preserves stale calculation data forever.** A sliding lifetime
  would let popular media avoid authoritative source and metadata reads indefinitely.
- **Metadata renewal hides a source change.** If source membership or matched width shared
  metadata's age, refreshing one input could silently extend another input that was never
  revalidated.
- **Absence hides unrelated generated widths.** Treating one missing selected width as
  whole-source absence would refuse usable rows after the current target changes.
- **A slow old read resurrects deleted or invalid data.** Completion order is not
  observation order; publishing whichever task finishes last can reverse regeneration.

## Why this shape

**Identity covers everything that determines the bytes, and nothing else.** Media
source, resolution, interval, tile geometry, thumbnail count, sprite index, sprite
version, Frame Index, and encoding quality. Each is there because varying it varies
the output; nothing else is there because varying it does not. Dropping any one input
makes two different artifacts share an identity, which is the collision above.

**The source version stamp comes from length and modification time.** The plugin needs
to know *which version* of a sprite file it read, and it has no cooperation from the
server: nothing announces a regeneration. Length and modification time are the
observable evidence, they cost one stat, and they change whenever the file is replaced
in the ordinary way. This is not a cryptographic guarantee and is not claimed as one —
a replacement that preserved both would go undetected, and the product accepts that.

**The stamp is taken once, and not re-validated.** The sprite's length and
modification time are snapshotted before decoding, and the plugin does not lock or
re-check a Jellyfin-owned file across encoding. A sprite replaced *during* a request
can therefore be served undetected. That is a deliberate trade, recorded in ADR 0002:
locking or revalidating files the plugin does not own would mean a protocol for
source mutation, in exchange for closing a window measured in milliseconds. The entry
is still correctly identified — by the version the plugin actually looked at.

**Stale entries are abandoned, never invalidated.** When a stamp changes, the identity
changes, so the entry path changes: old entries simply stop being reachable. The
alternative — finding and deleting them — would require watching Jellyfin's data for
changes, which the plugin cannot do, or scanning the tree on every request, which
costs more than the garbage. Unreachable entries cost space and nothing else, and
space is bounded by [resource bounds](resource-bounds.md).

**The ETag carries the stamp, so freshness is checkable by the client.** A client
holding a frame can confirm it cheaply, and a regenerated sprite breaks the match
without the client needing to know regeneration happened. The ETag is the promise
made observable.

**Disposition is reported but not promised.** `X-Trickplay-Cache` tells a caller
whether bytes were reused or generated. It is diagnostic: no hit rate is promised, and
a client that branches on it is depending on something the product does not commit to.

**Source facts, generated metadata, and Preview Cache Entries are different caches.** A
source observation proves only user-independent Item/source membership and the matched
Media Source video width. A metadata observation proves only interval, geometry, count,
and recorded width for one effective Source Video. Neither says anything about current
user authority, Source Sprite existence or version, JPEG bytes, or ETag identity. Keeping
the three sets separate lets HEAD reuse calculation inputs without turning that reuse into
representation evidence or allowing one input's renewal to extend another's age.

**Source-fact age is absolute and begins before source I/O.** Verified membership and
matched width live for 30 minutes; explicit user-independent Item, membership, or Source
Video absence lives for 5 minutes. A warm HEAD does not renew either. GET performs all
current user-scoped source checks and may publish only the positive facts it independently
verifies. A user-filtered failure is not shared absence, and invalid width or configuration
remains an error rather than becoming one.

**Metadata age is absolute and begins before I/O.** Independently validated positive rows
live for 30 minutes. Whole-source absence and selected-width absence or no-thumbnails live
for 5 minutes. Neither a HEAD hit nor a derived Frame Index renews age. Starting before the
host query prevents a slow read from receiving a fresh full lifetime when it finally
returns; a result already at its lifetime is rejected.

**GET is the freshness bridge.** It rereads current user, visibility, playback, membership,
Source Video, and matched width without consulting retained source facts, then publishes
the verified user-neutral positive from that source read's start. After current target
selection, GET may short-circuit only a current applicable metadata negative. Every other
GET reads authoritative metadata, even for a JPEG HIT or `304`, then publishes the checked
metadata observation from its own read start. This shortens HEAD staleness without allowing
cached calculation data to authorize a representation.

**Absence has the narrowest truthful scope.** An empty dictionary is whole-source absence.
A missing selected key or nonpositive count belongs only to that width, while other checked
rows remain usable. Invalid data and operational failure are not negative facts. They
renew nothing, and invalid selected data removes only the affected retained value.

**Read start, not completion, orders publication.** Source reads and metadata reads use
independent orders. Same-key reads may overlap without coalescing. An older-started task can
answer its own caller coherently, but cannot overwrite a newer positive or negative or
resurrect a value a newer invalid observation removed. The ordering evidence remains until
every older issued host read settles, then expires with the observations it protected.

## Where it is enforced

[Source resolution](../lifecycle/source-resolution.md) and
[Trickplay Frame Probe](../lifecycle/frame-probe.md) enforce source-fact and
generated-metadata freshness.
[Preview Cache Entry](../lifecycle/preview-cache.md) lists the independent representation
identity inputs and the resulting layout, ETag, and stamp.

## How a caller observes it

The representation half is visible as the ETag and a `304` when held bytes are still
current. The calculation half is visible only through bounded Frame Index behavior: HEAD
may temporarily report an index derived from older coherent source facts or metadata,
while GET rechecks current source authority and returns its actual index. After server-side
regeneration, GET returns `200` with a changed ETag when the resulting representation
identity changed.
