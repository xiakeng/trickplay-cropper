# Timeline isolation

## The promise

The Frame Timeline returns only the current interval and generated frame count. It does not
read a Source Sprite, access the Preview Cache, compare an entity tag, or encode an image, and
success does not promise that a later Preview can be served.

## What breaks without it

- Doing image work during Timeline calculation makes playback startup pay the cost of a frame
  that may never be displayed.
- Treating a Timeline as permission evidence would let a retained response outlive the user's
  current authorization.
- Treating it as representation evidence would hide changed metadata or a missing Source
  Sprite behind a response that is no longer current.

## Why this shape

The client needs stable playback inputs, not a server-selected frame. Returning the interval and
count once lets the client calculate direct Frame Index values locally while each Preview keeps
its own current-user authorization, metadata, range, and Source Sprite checks. Keeping the two
operations separate also avoids a replacement cache or a coherence protocol for data Jellyfin
owns.

## Where it is enforced

[Source resolution](../lifecycle/source-resolution.md) performs the authorized metadata read;
[Frame Timeline](../lifecycle/README.md) stops at the calculation result before representation
work.

## How a caller observes it

Timeline success is a `200` JSON response with exactly `intervalTicks` and `frameCount`, with no
ETag or `Last-Modified`. A later Preview may still return `401`, `403`, `404`, or `500` after
repeating its own current checks.
