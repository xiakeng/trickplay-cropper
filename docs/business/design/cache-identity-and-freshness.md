# Cache identity and freshness

## The promise

A cached JPEG remains identified by the Source Sprite version and the direct frame it
represents. Current authorization and generated metadata are re-established before a Preview
uses that entry.

## Why this shape

Preview identity includes the cache namespace, effective Media Source, frame and tile geometry,
Source Sprite index, Source Sprite length and UTC last-write ticks, direct Frame Index, and JPEG
quality. These are the values that determine the encoded bytes. The interval and thumbnail
count are deliberately excluded: interval does not affect a direct-index image, and count is
only the request's range boundary.

The source version stamp uses the Sprite's length and modification time. A replacement changes
the cache path and opaque strong ETag, leaving the old entry unreachable for scheduled cleanup.
The stamp is taken once; the plugin does not promise detection of a Sprite replaced mid-request.

Every valid Preview performs the current-user boundary and one authoritative metadata read,
including cache hits and conditional requests. A matching `If-None-Match` is compared only after
that work and returns `304` without reading or encoding JPEG bytes. Timeline data is not a cache
validator, permission token, or representation version.

## Where it is enforced

[Preview Cache Entry](../lifecycle/preview-cache.md) defines the identity and layout;
[Source resolution](../lifecycle/source-resolution.md) defines the authoritative request
boundary; [Cache coordination](../lifecycle/cache-coordination.md) defines safe publication.

## How a caller observes it

The ETag is an opaque strong validator. `X-Trickplay-Cache` reports HIT or MISS for diagnostics
only; clients must not use it as a freshness or correctness signal.
