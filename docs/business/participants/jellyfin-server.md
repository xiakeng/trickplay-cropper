# Jellyfin Server

## Owns

Everything durable. The plugin derives from all of it and owns none of it.

| Owned | Supplied to the plugin as |
|---|---|
| The library, Items, users, and playback authorization | A user-scoped Item lookup and an authorization decision |
| Media Sources, including local alternate versions | Membership of a source in a logical video, and the effective Source Video |
| The trickplay configuration | The current Trickplay Resolution Targets |
| Generated trickplay metadata | Interval, tile geometry, thumbnail count, and recorded width per Media Source |
| The Source Sprites | JPEG files the plugin crops frames out of |
| Temporary storage | The space the Cache Tree lives in |
| The scheduled task host | When the cleanup run happens |

## May change at any time

This is the fact that shapes the whole product, so it is stated here rather than
left implicit:

- **The Trickplay Resolution Targets may be added to, reduced, or emptied** by an
  administrator at any moment, and the generated metadata is not kept in step with
  them. A target removed from the configuration keeps its recorded metadata; a
  target added may generate a new row.
- **Trickplay data may be regenerated**, replacing Source Sprite files in place and
  rewriting metadata. A refresh with replacement deletes recorded data before
  regenerating.
- **A target may not survive contact with a video.** Generation normalizes a
  requested width — an odd width rounds down to even, and a width larger than the
  video clamps to it — so the recorded width can differ from the target that
  produced it.
- **Recorded metadata is a snapshot**, not a live projection of configuration. Its
  interval, geometry, and counts describe what was generated then.
- **Metadata does not prove a file exists.** Recorded data can outlive the sprite
  it describes, and a sprite can exist without a row.

None of this is a defect to work around; it is the operating condition. The plugin's
response to it — one exact Selected Trickplay Resolution with no fallback, and an
entry identity that includes the sprite's version — is in
[resolution exactness](../design/resolution-exactness.md) and
[cache identity](../design/cache-identity-and-freshness.md).

## Must not be asked to

The plugin never writes to anything the server owns. It does not generate
trickplay data, does not repair or regenerate a Source Sprite, does not modify
metadata, does not change configuration, and does not write outside the Cache Tree.
The boundary that keeps this true is in [the Cache Tree](cache-tree.md).

## Does not decide

**Which resolution a request should use.** The server exposes a set of targets and a
set of recorded widths, and chooses nothing between them. The selection is the
plugin's policy, and it is the reason
[source resolution](../lifecycle/source-resolution.md) exists at all.

## Evidence

The server-side interfaces the plugin reads through, and the version-specific
behaviour behind every claim above, are recorded in the research notes under
[the trickplay resolution contract](../../research/jellyfin-10.11.11-trickplay-resolution-contract.md)
and [the administration API contract](../../research/jellyfin-10.11.11-administration-api-contract.md).
This chapter states the boundary; those notes are the evidence.
