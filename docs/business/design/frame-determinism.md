# Frame determinism

## The promise

The same playback position always selects the same frame, and no playback position is
out of range.

## What breaks without it

- Two clients scrubbing to the same moment see different frames, and any feature
  built on agreeing about a position — a shared timestamp, a clip reference, a
  reported frame — stops meaning anything.
- The end of a video stops having a preview. Positions past the last generated frame
  are ordinary, not exceptional: a video usually runs beyond its final interval.
- A wrong crop is delivered confidently. This is the serious one, and it is silent:
  the response is a valid JPEG, so nothing downstream can tell.

## Why this shape

**Clamping is a business rule, not an error path.** A position beyond the last
generated frame resolves to the last frame. The alternative — refusing those
positions — would mean the tail of every video has no preview, and would push the
problem onto every client, each of which would invent its own clamp. Deciding it once,
here, is what makes the answer the same for everyone.

**The geometry is recomputed per request rather than stored.** Sprite index, cell,
row, column, and crop offsets all follow from the Frame Index and the recorded tile
geometry, so storing them would create a second source of truth about geometry that
could disagree with the first. Recomputation is cheap arithmetic; divergence is not.

**Every intermediate value is checked for overflow.** Tile geometry and thumbnail
counts are recorded by the server and unbounded as far as the plugin is concerned.
Arithmetic that overflows produces a plausible-looking but wrong crop, which is the
silent failure above. Failing the request with diagnostics attached is strictly
better than delivering a wrong frame.

**Recorded geometry is verified against the actual sprite.** The crop comes from
metadata; the pixels come from a file. If the file's dimensions do not equal tile
width × frame width by tile height × frame height, the metadata does not describe
that file, and every offset derived from it is wrong. Trusting the metadata would
produce exactly the confident wrong frame this chapter exists to prevent, so the
mismatch fails the request instead. This is the limit of the product's trust in
Jellyfin's data: sprites are trusted, within a geometry check. ADR 0002 records that
boundary.

**A sprite that cannot be decoded is an operational failure, not a "no preview
here".** The sprite was already established to exist, so a decode failure means
something changed or broke underneath the request. Collapsing that into `404` would
tell a client "there is no preview at this position" when the truth is "this request
could not be completed" — and the client's correct response to those two differs.

## Where it is enforced

[Frame Selection](../lifecycle/frame-selection.md) for the derivation and its checks,
and [preview generation](../lifecycle/preview-generation.md) for the geometry
verification against the sprite.

## How a caller observes it

`X-Trickplay-Frame-Index` on every successful probe and preview response, including a
preview `304`, and the Frame Index independently inside the preview ETag. A caller
that records both can verify determinism directly: the same position yields the same
index, and the same representation yields the same ETag for as long as the source
version holds.
