# Frame determinism

## The promise

Each direct Frame Index identifies exactly one generated frame, and the server never silently
changes an invalid index into another frame.

## What breaks without it

- Clamping or guessing would return a valid JPEG for the wrong caller-selected frame.
- Unchecked geometry arithmetic could produce a plausible crop from the wrong cell.
- Trusting mismatched Sprite dimensions could deliver a confident but incorrect image.

## Why this shape

The client owns playback-position arithmetic from the retained Frame Timeline. The server's
boundary is simpler and safer: accept only `0 <= FrameIndex < ThumbnailCount`, then derive the
Source Sprite, row, column, and crop from one authoritative metadata row. There is no end
clamping or server-side position selection.

All intermediate geometry conversions are checked. The actual Sprite dimensions are validated
against recorded tile geometry before encoding; decode or geometry failures are operational
errors, not an unavailable-frame answer.

## Where it is enforced

[Frame Selection](../lifecycle/frame-selection.md) validates the direct index and derives the
crop. [Preview generation](../lifecycle/preview-generation.md) validates the Sprite before
encoding.

## How a caller observes it

The requested Frame Index is represented by the returned JPEG and its opaque strong ETag. The
index is not emitted in a response header; clients must treat ETags as opaque validators.
