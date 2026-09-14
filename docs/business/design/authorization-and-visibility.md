# Authorization and visibility

## The promise

A Timeline or Preview is returned only to a caller with a current user who may play the
logical video. An Item the caller cannot see is indistinguishable from an Item that does not
exist.

## Why this shape

Visibility is checked before playback access so a hidden Item cannot be enumerated. The logical
Item and selected Media Source are resolved through user-scoped Jellyfin APIs, and the Source
Video must also be visible. Membership in the playable logical video's source enumeration is
the authorization boundary; the Source Video is not subjected to a second playback-policy check.

An authenticated server API key without a current user is `403`, not an implied user. A prior
Frame Timeline, JPEG, ETag, or any retained client state never substitutes for these checks:
both operations authorize every request and read current generated metadata.

## Where it is enforced

[Source resolution](../lifecycle/source-resolution.md) describes the ordered current-user
boundary and its status mapping.

## How a caller observes it

`401` means no usable authentication/current user, `403` means authenticated denial, and `404`
conceals an invisible or unavailable Item, source, or generated metadata. The same boundary
applies to Timeline and Preview.
