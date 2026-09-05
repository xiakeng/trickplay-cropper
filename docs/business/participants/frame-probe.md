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

Everything the shared front of the pipeline establishes, because the probe runs it:
the caller is a real user, the Item is visible to them, they may play it, the Media
Source belongs to it, and one exact Selected Trickplay Resolution applies. See
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

The client, and only the client. Internally it shares the pipeline with
[the preview request](preview-request.md) and diverges from it at a single point;
where that point is, and what lies beyond it, is drawn in
[the lifecycle layer](../lifecycle/frame-probe.md).
