# Cache identity and freshness

## The promise

A cached preview is served only while it still belongs to the source version that
produced it.

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

## Where it is enforced

[Preview Cache Entry](../lifecycle/preview-cache.md), which lists the identity inputs
and the resulting layout, ETag, and stamp.

## How a caller observes it

The ETag, and a `304` when a held frame is still current. A client can verify the
promise end to end: hold a frame, ask again, get `304`; and after a server-side
regeneration, ask again and get `200` with a different ETag rather than the old bytes.
