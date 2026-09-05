# The cleanup run

The plugin acting as its own janitor: the party that deletes.

## Owns

- **Deletion inside the Cache Tree, and nothing else.** The run's whole authority is
  to remove Preview Cache Entries, the temporary files beside them, and the empty
  directories left behind. See [the Cache Tree](cache-tree.md) for the boundary it
  may not cross.
- **Its own politeness.** It decides which files it is allowed to consider, and
  refuses the rest. What it refuses, and why, is in
  [scheduled cleanup](../lifecycle/scheduled-cleanup.md).

## Does not own

- **When it runs.** The schedule belongs to the server: the run is a Jellyfin
  scheduled task, so an administrator sees it, reschedules it, and may trigger it by
  hand. The plugin does not decide its own cadence and has no internal timer.
- **Whether an entry is still wanted.** Nothing consults clients, and no entry is
  kept because someone might ask again. An entry becomes garbage when its identity
  can no longer be computed — see
  [cache identity](../design/cache-identity-and-freshness.md) — and the run does not
  need to know why.

## Must not

- **Disturb a request in flight.** Two protections keep this true, a cutoff taken
  when the run starts and a re-check at the moment of deletion. Both are mechanism,
  and both are in [scheduled cleanup](../lifecycle/scheduled-cleanup.md); the
  promise they serve is [resource bounds](../design/resource-bounds.md).
- **Delete what it does not recognize.** A file the run cannot classify is skipped,
  never removed. It is a guest in the server's temporary storage, not its owner.
- **Repair, regenerate, or reclaim.** The run only deletes. Nothing it does can
  produce a frame, fix a stale one, or restore anything Jellyfin lost.

## Faces

[The Cache Tree](cache-tree.md), which it empties, and indirectly every preview
request, whose in-flight work it must step around. It never faces the client: a
cleanup is invisible to callers except as a subsequent cache miss.
