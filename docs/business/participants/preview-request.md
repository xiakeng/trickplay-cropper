# The preview request

The plugin acting as the provider of one artifact: a **Trickplay Preview**, a single
JPEG frame cropped from a Source Sprite.

## Owns

- **The bytes.** One JPEG frame at a fixed quality, cropped from a sprite Jellyfin
  generated. The plugin never creates a sprite, never adds frames to one, and never
  repairs one.
- **The identity of those bytes.** The ETag, and the
  [Preview Cache Entry](../lifecycle/preview-cache.md) that holds them. What makes
  two previews the same is the plugin's decision alone.
- **The cost of producing them.** How much of a sprite is decoded, and how many
  decodes may run at once, are the plugin's to bound:
  [preview generation](../lifecycle/preview-generation.md).

## May assume

A current user exists, the logical Item and Source Video are visible to that user, the
user may play the logical video, the selected source belongs to the user-shaped Media
Source enumeration, and the exact shared calculation succeeded. It also requires a
Source Sprite for the selected frame to resolve and exist. The Frame Probe shares only
the calculation; it establishes none of GET's user authority and never reaches the
sprite gate. See [the Trickplay Frame Probe](frame-probe.md).

## Must not

- **Substitute a resolution.** If the generated metadata does not match the Selected
  Trickplay Resolution exactly, there is no preview, and the plugin does not offer a
  nearby one. Why this is non-negotiable is in
  [resolution exactness](../design/resolution-exactness.md).
- **Serve a frame it cannot vouch for.** A sprite whose dimensions do not match the
  recorded geometry fails the request rather than being cropped on trust. See
  [frame determinism](../design/frame-determinism.md).
- **Read or write outside the Cache Tree**, or follow a path that leaves it. The
  boundary is in [the Cache Tree](cache-tree.md).
- **Hold a lock past the point of usefulness.** A request never depends on a file it
  has stopped guarding; the rule that makes this true is a coordination rule, in
  [concurrency safety](../design/concurrency-safety.md).

## Faces

The client on one side, the Cache Tree and the encoder on the other. The order in
which it deals with them is the spine of
[the lifecycle layer](../lifecycle/README.md).
