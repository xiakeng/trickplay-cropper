# The cleanup run

The plugin acting as its own janitor: the party that deletes.

## A GET arriving during cleanup

Cleanup walks the Cache Tree without holding a whole-tree lease. Synchronization starts only
when it reaches a candidate: a final entry or matching temporary file uses a shared tree lease
and that entry's lock, while an orphan temporary file or directory prune uses the exclusive tree
lease. A new GET for another entry continues; a GET for the same entry waits for its lock, and a
GET that meets an exclusive lease waits until cleanup releases the tree. The candidate is
re-checked before deletion, so a changed or missing file is left alone.

```mermaid
sequenceDiagram
    autonumber
    participant T as Cleanup task
    participant L as Cache Tree lease
    participant E as Entry lock
    participant G as New Preview GET
    participant F as Cache entry

    T->>T: Discover a candidate at the run cutoff
    alt Final entry or matching temporary file
        T->>L: Acquire shared lease
        L-->>T: Granted
        T->>E: Acquire candidate lock
        E-->>T: Granted
        G->>L: Request shared lease
        L-->>G: Granted (shared readers coexist)
        G->>E: Acquire the same entry lock
        Note over G,E: Waits if cleanup owns this entry
        T->>T: Re-check presence, fingerprint, and reparse status
        alt Still the captured candidate
            T->>F: Delete candidate
        else Changed, missing, or reparse point
            T->>F: Leave candidate
        end
        T->>E: Release
        E-->>G: Granted
        G->>F: Read the final entry
        alt Entry exists
            F-->>G: HIT; buffer the JPEG
        else Entry was deleted
            G->>F: Generate, publish, and buffer
            F-->>G: MISS
        end
        G->>E: Release
        G->>L: Release shared lease
        T->>L: Release shared lease
    else Orphan temporary file or directory prune
        T->>L: Acquire exclusive lease
        L-->>T: Granted
        G->>L: Request shared lease
        L-->>G: Wait (writer-preferred)
        T->>F: Delete orphan or prune directory
        T->>L: Release exclusive lease
        L-->>G: Granted shared lease
        G->>E: Acquire entry lock
        E-->>G: Granted
        G->>F: Read or generate its entry
        F-->>G: HIT or MISS
        G->>E: Release
        G->>L: Release shared lease
    end
```

The GET is never interrupted by cleanup: it either finishes with bytes it read before deletion,
or waits and then reads or regenerates the entry after cleanup. If the GET acquires the same
entry first, cleanup waits for that lock and performs the same re-check afterward. A different
entry needs only its own entry lock, so it can proceed while a shared-lease cleanup candidate is
being processed.

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
  kept because someone might ask again. An entry becomes garbage when current request
  inputs no longer compute its path — see
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
