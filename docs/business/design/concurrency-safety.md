# Concurrency safety

## The promise

No caller reads a partially written entry, two callers asking for the same frame pay
for one generation, emptying the Cache Tree never disturbs a request in flight, and a
caller that gives up leaves nothing held behind.

## What breaks without it

- **Torn JPEGs reach clients.** A reader that finds a file mid-write serves part of an
  image. The client cannot distinguish that from a corrupt sprite.
- **Duplicate generation.** Under a scrub storm, several callers miss on the same
  frame and each decodes it. Decode is the expensive part of the whole product, so
  contention multiplies cost exactly when the server is already busiest.
- **Cleanup deletes live work.** A maintenance run that removed an entry while a
  request was generating it would fail a request that had already succeeded.
- **Deadlock, or starvation.** Two locks taken in different orders by different paths
  deadlock; a stream of requests can starve the run that is supposed to bound the tree.
- **Leaked holds.** A caller that disconnects mid-wait, still holding a lock, blocks
  every later caller for that entry until the process restarts.

## Why this shape

**Not global serialization.** One lock over the whole Cache Tree would be simple and
would turn every preview into a queue: two callers asking for frames of different
videos, in different sprites, would wait for each other for no reason. The design
coordinates per entry, so unrelated requests are genuinely concurrent, and uses a
tree-wide lease only where the *shape* of the tree changes. ADR 0001 records this
choice.

**Two locks, one order, always.** Tree lease then entry lock, released in reverse, on
every path including the maintenance ones. A consistent order is what makes deadlock
impossible between them — not a timeout, not a detection scheme, just never taking
them the other way round.

**Shared by default, exclusive only for shape changes.** Requests take the tree lease
shared, so they never wait for each other at that level. Only removing an orphaned
temporary file or pruning an empty directory takes it exclusively, because those are
the operations that change what paths exist. Everything else is a file operation on a
path that already means something.

**Writer-preferred, so the run cannot starve.** Once a maintenance operation waits for
the exclusive lease, new requests queue behind it instead of streaming past. Without
preference, a busy server would defer cleanup indefinitely, and the tree would grow
for exactly as long as it was being used — the opposite of the intent.

**Buffer before release.** The response is read into memory before either lock is
released. This looks like an inefficiency and is the load-bearing rule: once the entry
lock is released, the maintenance run is free to delete that entry, because from its
point of view it is an ordinary file older than its cutoff. A response still referring
to the file could then fail after having succeeded. Buffering makes a released entry
nobody's problem.

**Write temporary, publish atomically, and lose gracefully.** Generation writes beside
the final path and moves into it with an operation that refuses to overwrite. The
refusal is the entire cross-process story: two writers, whichever move lands first
wins, and the loser reads the winner's entry and reports a hit. Losing is not an error
and is not retried, because both writers produced equivalent bytes and retrying would
be a second generation. ADR 0001 names this the final guard against activity outside
the process, which no in-process lock can reach.

**No timeouts, only cancellation.** A timeout would have to pick a duration that is
too short for a contended entry and too long to matter. Cancellation is exact: a caller
that disconnects stops waiting immediately and holds nothing. Every wait in the design
is cancellable for that reason.

**Reference-counted entry locks, so the registry does not grow.** An entry lock exists
while someone holds or waits on it, and is discarded when the count reaches zero. The
alternative — keeping a lock per path forever — would make the registry a second,
unbounded cache of every path ever requested, which is its own leak.

**Paths are re-checked, and reparse points are refused.** The tree lives in storage the
plugin does not control, and entry paths are built from values derived from server
state. A symbolic link planted in the tree would otherwise redirect a write outside it,
and a crafted identity would otherwise reach a path that was never an entry. Both are
refused rather than followed, on every access and not only at creation.

**The waits are observable, because a guarantee nobody can check is a hope.** The
structured Debug protocol exposes the cache disposition, the entry-lock wait and
ownership, the Cache Tree lease wait, and the decode-permit wait, with the Frame Index
and sprite index for correlation — see
[what is observable](../lifecycle/cache-coordination.md). The events are deliberately
*observations*, not gates: whether a wait occurred depends on scheduling luck, so
nothing may pass or fail on their presence. Their deterministic ordering and
cancellation behaviour are verified by component tests instead.

## Where it is enforced

[Cache coordination](../lifecycle/cache-coordination.md), which draws the acquisition
order, the two-caller case, and the publication race.

## How a caller observes it

`Server-Timing`, which reports the lookup, cache, decode, and encode stages with their
durations. A wait appears there as time rather than as a named event, so a slow preview
can be attributed to a stage without server access — and the difference between "the
cache is contended" and "the encoder is saturated" is visible, which matters because
the two have opposite remedies.
