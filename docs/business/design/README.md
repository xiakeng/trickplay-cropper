# Design

What the product promises, what breaks without each promise, and why the mechanism
is shaped the way it is. This layer never describes a mechanism: every "how" is a
link into [the lifecycle layer](../lifecycle/README.md), and every "who owns it" is
a link into [the participants layer](../participants/README.md).

Read this layer last. It makes sense once you know what happens, and it is the layer
that answers *why not something else*.

## The guarantee map

| Promise | Chapter | Enforced in | A caller observes it as |
|---|---|---|---|
| A frame reaches only someone who may play it, and an invisible Item does not exist | [Authorization and visibility](authorization-and-visibility.md) | [Source resolution](../lifecycle/source-resolution.md) | `401`, `403`, `404` |
| The width served is one the server was configured to produce, exactly | [Resolution exactness](resolution-exactness.md) | [Source resolution](../lifecycle/source-resolution.md) | `404`, never a substitute |
| One position always selects one frame, and no position is out of range | [Frame determinism](frame-determinism.md) | [Frame Selection](../lifecycle/frame-selection.md) | `X-Trickplay-Frame-Index`, the ETag |
| Asking which frame never costs image work, and never promises delivery | [Probe isolation](probe-isolation.md) | [Trickplay Frame Probe](../lifecycle/frame-probe.md) | Two headers, no body |
| A cached preview is served only while it belongs to its source version | [Cache identity and freshness](cache-identity-and-freshness.md) | [Preview Cache Entry](../lifecycle/preview-cache.md) | The ETag, `X-Trickplay-Cache` |
| Nobody reads a partial entry, and one frame costs one generation | [Concurrency safety](concurrency-safety.md) | [Cache coordination](../lifecycle/cache-coordination.md) | `Server-Timing` |
| The Cache Tree stays bounded, and emptying it disturbs nobody | [Resource bounds](resource-bounds.md) | [Scheduled cleanup](../lifecycle/scheduled-cleanup.md) | Nothing — no caller can observe this |

```mermaid
flowchart TD
    G["GET authorization and visibility<br/>carries: authorization and visibility"] --> S2["Resolution selection<br/>carries: resolution exactness"]
    H["HEAD source facts<br/>carries: probe non-authority"] --> S2
    S2 --> S3["Frame Selection<br/>carries: frame determinism"]
    S3 --> P["The probe stops here<br/>carries: probe isolation"]
    S3 --> S4["Cache lookup and generation<br/>carries: cache identity,<br/>concurrency safety"]
    S4 --> S5["The Cache Tree over time<br/>carries: resource bounds"]
```

The two stages carrying the most are GET's authorization front, where visibility and
playback authority must remain ordered, and the cache, where identity and coordination
are two promises about the same file. HEAD joins only at calculation and makes no
authorization promise.

## Deliberate non-promises

What the product refuses to guarantee is as much a design decision as what it does
guarantee:

- **No nearest resolution.** If the exact Selected Trickplay Resolution has no
  generated metadata, there is no preview. See
  [resolution exactness](resolution-exactness.md).
- **No probe-implies-preview.** A successful probe says nothing about whether the
  sprite exists. See [probe isolation](probe-isolation.md).
- **No client cache lifetime.** The plugin supplies identity and freshness and
  prescribes no key, expiry, or invalidation rule. See
  [the client](../participants/client.md).
- **No repair of Jellyfin-owned data.** A wrong, stale, or missing Source Sprite is
  the server's to fix. See [Jellyfin Server](../participants/jellyfin-server.md).
- **No persistence of anything derived.** The whole Cache Tree is reclaimable at any
  time, by the plugin or by the server. See [the Cache Tree](../participants/cache-tree.md).
- **No detection of a sprite replaced mid-request.** The source version is snapshotted
  once and not revalidated across encoding, so a replacement inside that window can be
  served undetected. See
  [cache identity and freshness](cache-identity-and-freshness.md).
- **No content negotiation.** Nothing negotiates type, dimensions, or quality. One
  frame, one format, one quality, and a client that cannot display it still receives
  exactly that.

## How to read a chapter here

Every chapter has the same five sections: the promise, what breaks without it, why
the design has this shape and which alternatives were rejected, where it is
enforced, and how a caller can observe it. An empty "where it is enforced" would be
a defect in the product rather than in the document, which is what makes the layer
auditable.
