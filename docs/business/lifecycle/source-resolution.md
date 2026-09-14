# Source resolution

_Why authorization is repeated for every operation and why no resolution fallback exists:
[Authorization and visibility](../design/authorization-and-visibility.md) and
[Resolution exactness](../design/resolution-exactness.md). This chapter is the mechanism._

## Current-user source boundary

Both `FrameTimeline` and `Preview` first require Jellyfin's ordinary endpoint policy and a
usable current user. The plugin then performs these checks in order:

1. Resolve the logical Item through the user-scoped library API; an invisible Item is `404`.
2. Require `PlayAccess.Full` on that logical video; denial is `403`.
3. Enumerate the user's playback Media Sources and require the requested source (or Item-ID
   default) to be an exact member.
4. Resolve the selected Source Video through the user-scoped library API and require its exact
   identity; absence is `404`.

A server API key without a current user is `403`. Membership in the playable logical video's
source enumeration is the authorization boundary; the Source Video is not checked for a second
playback policy.

## Selected Trickplay Resolution

After source authorization, each operation reads the current server Trickplay Resolution
Targets, chooses the minimum target, clamps it to the matched source video's width when
smaller, and applies Jellyfin's required even-width normalization. The result is the
**Selected Trickplay Resolution**. Generated metadata must contain an exact row for it.

There is no default, nearest, or alternate-width fallback. No configured target or unavailable
exact row is `404`; unreadable configuration and internally invalid generated metadata are
`500`.

## Authoritative metadata

Each successful authorization is followed by one plugin-owned authoritative generated-metadata
read for the selected Source Video and resolution. Timeline requires positive interval and frame
count. Preview requires positive frame count and valid frame/tile geometry; its representation
does not depend on interval being positive. A valid Preview index must satisfy
`0 <= FrameIndex < ThumbnailCount` and an invalid value is `400`.

Timeline ends after returning `intervalTicks` (checked milliseconds-to-Jellyfin-ticks conversion)
and `frameCount`; it does not resolve a Source Sprite, touch the Preview Cache, compare a
conditional request, or encode. Preview continues to the GET-only Source Sprite existence and
geometry checks, then cache and encoding. Metadata and Source Sprite reads are independent; the
product does not promise an atomic snapshot.

## Failure mapping

| Condition | Timeline | Preview |
|---|---:|---:|
| Malformed binding or out-of-range FrameIndex | `400` | `400` |
| Unauthenticated or no usable current user | `401` | `401` |
| Authenticated denial or userless API key | `403` | `403` |
| Concealed/absent Item, source, or exact metadata | `404` | `404` |
| Missing Source Sprite | — | `404` |
| Invalid configuration/data or processing failure | `500` | `500` |

## Anchors

`JellyfinPreviewContextResolver.ResolveAuthorizedSourceAsync` owns the shared current-user
source boundary. `JellyfinTrickplayFrameCalculationResolver` selects the target, normalizes the
width, reads metadata, and validates either Timeline or Preview inputs. `TrickplayFrameTimeline`
returns the calculation model; `TrickplayPreview` continues through
`JellyfinPreviewSourceResolver` to representation work. `PreviewQuery` and
`PreviewSourceQuery` carry Item and optional Media Source identifiers.
