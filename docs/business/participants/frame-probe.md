# The Trickplay Frame Probe

The plugin acting as the authority on one narrow question: *which frame does this
position select?*

## Owns

- **The Frame Index answer.** Given a position, an Item, and an optional Media
  Source, the probe owns the arithmetic that turns them into a Frame Index. How it
  derives it is in [Frame Selection](../lifecycle/frame-selection.md).
- **The decision to stop.** The probe owns the boundary between answering the cheap
  question and doing the expensive work, and never crosses it.

## May assume

Jellyfin's ordinary endpoint policy accepted the request, the unscoped logical Item
and effective Source Video have their requested identities, the requested Media Source
belongs to the logical Item's full host enumeration, and one exact Selected Trickplay
Resolution applies. It may not assume a current user exists, the Item is visible to a
particular user, or anyone may play it. See
[source resolution](../lifecycle/source-resolution.md).

## Must not

- **Touch an image.** It does not resolve, stat, open, or snapshot a Source Sprite,
  and does not reach the Cache Tree or the encoder. This is structural, not a
  promise of restraint — the reason it is structural is in
  [probe isolation](../design/probe-isolation.md).
- **Claim the frame is obtainable.** It answers "which frame" and not "is it there".
  A client that treats the answer as a promise will be surprised by the `404` that
  can follow. See [the client](client.md).
- **Disclose anything about the bytes.** No ETag, no conditional behaviour, nothing
  that would let a caller infer image content from a probe. The exact header set is
  in [the response contract](../lifecycle/response-contract.md).

## Faces

The client, and only the client. Internally it shares resolution and Frame Index
calculation with [the preview request](preview-request.md), while each operation owns a
different source and authorization path. The split and the probe's stopping point are
drawn in [the lifecycle layer](../lifecycle/frame-probe.md).
