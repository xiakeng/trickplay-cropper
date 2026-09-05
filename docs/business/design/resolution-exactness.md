# Resolution exactness

## The promise

The width a caller receives is a width the server was configured to produce for that
Media Source, exactly. Never a substitute.

## What breaks without it

A fallback does not fail safely, it fails *plausibly*. The crop is computed from
generated geometry — tile size, frame size, thumbnail count. Serve a resolution whose
metadata was not matched, and every offset in the crop is derived from geometry that
does not describe the sprite being cropped. The result is a real JPEG of the wrong
part of the wrong sprite: a piece of the neighbouring cell, a sliver across two
frames, or a frame from a different video's data at a coincidentally matching path.

A client cannot detect this. It receives `200` and an image. That is why the failure
mode is worse than an error.

## Why this shape

**The server does not choose, so the plugin must.** Jellyfin exposes a set of
Trickplay Resolution Targets and, separately, a set of recorded widths per Media
Source, and picks nothing between them. "Read the Jellyfin setting" is not a policy
when several targets exist. See
[Jellyfin Server](../participants/jellyfin-server.md).

**The minimum target is chosen.** Not the maximum, not the first, not the closest to
some preferred value. The minimum is the width every configured client can display,
and — because the choice is stable across configuration edits that add larger targets
— it keeps the Preview Cache Entry identity stable. A choice that varied with
unrelated configuration changes would silently invalidate the whole tree.

**Jellyfin's normalization is mirrored, not guessed.** Generation rounds an odd width
down to even and clamps a width larger than the video down to the video's width, so
the recorded width can legitimately differ from the target that produced it. A raw
equality test between a target and a recorded key is therefore not universally valid,
and the plugin applies the same normalization instead of comparing raw numbers.

**No fallback of any kind.** Four alternatives were considered and rejected:

- *Default to 320 px* — the plugin's original behaviour, and the one this promise
  exists to remove. It silently substitutes a width the administrator may never have
  configured, and it survives configuration changes that should have changed the
  output.
- *Take another current target when the minimum has no metadata* — serves a
  resolution the caller's client may not expect, and makes the answer depend on which
  targets happen to have data.
- *Take the nearest recorded width* — the most dangerous of the four, because it
  always finds something and therefore always produces a plausible wrong frame.
- *Treat recorded metadata as authoritative and serve whatever exists* — abandons
  selection entirely, so the served width depends on generation history rather than
  on configuration.

**A configuration failure is a `500`, not a fallback.** Unreadable configuration, or
a target that normalizes to a non-positive width, fails loudly. Substituting a width
here would hide a broken server behind working-looking previews.

**The exact-match rule knowingly refuses servable data.** An odd or oversized target
can leave recorded tiles on disk under a key that differs from the Selected Trickplay
Resolution. The tiles exist; the policy still returns `404`, and logs both the targets
and the recorded widths so the mismatch is diagnosable. Serving them would mean
cropping with metadata the plugin did not match.

## Where it is enforced

[Source resolution](../lifecycle/source-resolution.md), in the selection view: minimum
target, normalization, exact match.

## How a caller observes it

`404` when no generated metadata matches the Selected Trickplay Resolution exactly,
and `500` when the configuration itself cannot be trusted. Never a `200` at an
unexpected width.
