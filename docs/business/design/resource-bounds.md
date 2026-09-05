# Resource bounds

## The promise

The Cache Tree does not grow without bound, and emptying it never disturbs a request
in flight.

## What breaks without it

- **Temporary storage fills.** The tree only ever gains entries: a new frame, a new
  sprite version, a new resolution after a configuration change. Nothing removes them
  as a side effect of serving requests, so without a run the tree grows for the life of
  the server.
- **The plugin becomes responsible for a full disk.** It writes into space it does not
  own. Filling it degrades the server, not just the plugin, and the plugin has no
  standing to decide that trade.
- **A run that is not careful deletes live work.** The obvious implementation — walk the
  tree and delete old files — removes entries a request is mid-generation on, failing
  requests that had already succeeded. Bounding the tree is not worth that.
- **Orphans accumulate.** A process killed between writing a temporary entry and
  publishing it leaves a file that no identity will ever name again. Nothing else in
  the design can remove it, because nothing else knows it is not an entry.

## Why this shape

**Entries become garbage without ever becoming invalid.** When a sprite version changes,
the identity changes and the old entries stop being reachable — see
[cache identity](cache-identity-and-freshness.md). No invalidation runs, so reclamation
is the only mechanism that bounds the tree, and it can be crude: it does not need to know
*why* an entry is unreachable, only that nothing will ask for it again.

**The schedule belongs to the server, not the plugin.** The run is a Jellyfin scheduled
task, so an administrator sees it, reschedules it, and may trigger it by hand. A plugin
with an internal timer would consume host resources on a cadence nobody chose and nobody
can see. This also means the run has no opinion about how often it should happen: the
product's requirement is only that it happens.

**One run at a time.** A second trigger while one is running does not start a parallel
run. Two concurrent runs would each classify files the other was about to delete, and the
accounting of what was removed would be meaningless. Bounding reclamation is not worth
unbounded concurrency in the reclaimer.

**A cutoff taken at the start, not per file.** The run records one moment and considers
only files last written at or before it. This is the cheapest possible way to make
in-flight work untouchable, and it needs no coordination with requests: anything a
request creates after the run began is simply outside the run's view. The alternative —
asking the coordination layer what is live — would couple cleanup to request state and
make the run wait on callers.

**Classification before deletion, and unrecognized means skip.** A discovered file is a
final entry, a temporary entry belonging to an entry path, another kind of temporary
file, or something the run does not recognize. Only the first three are candidates, and
they are removed under different locks because they mean different things — see
[concurrency safety](concurrency-safety.md). The fourth is left alone: the run is a guest
in the server's temporary storage and has no business deleting a file it cannot account
for. Guessing would turn a bounded cache into a liability.

**Re-check at the moment of deletion.** Time passes between discovering a file and
deleting it, and a file replaced in that window is not the file that was judged eligible.
Verifying that it is still present and unchanged costs one stat and closes the window. The
same check refuses reparse points, for the reason given in
[the Cache Tree](../participants/cache-tree.md).

**Pruning happens last, and exclusively.** Deleting entries leaves empty directories, and
removing a directory is a change to the tree's shape rather than to its contents, so it
takes the exclusive lease and runs bottom-up: a parent becomes a candidate only once its
children are gone. Pruning first would race a request about to create a file inside.

**A high skip count is success, not failure.** The run reports what it removed and what it
skipped. Skips mean the tree was busy while the run walked it and the protections did
their job; treating them as errors would push an implementation toward deleting anyway.

## The other bound: what generation may cost the host

The tree is not the only thing that needs bounding. Generating a preview decodes part
of a JPEG with native code, inside the Jellyfin server process, sharing it with playback,
scanning, and everything else the server does. An unbounded number of concurrent decodes
under a scrub storm would starve the host, and the plugin has no standing to make that
trade on the administrator's behalf.

Generation therefore waits for one of a small fixed number of **decode permits** before
opening a sprite. The bound is a statement about being a tenant, not a performance
tuning value: it caps what Trickplay Cropper can cost the server no matter how many
clients ask at once. Waiting is cancellable and has no timeout, for the reason given in
[concurrency safety](concurrency-safety.md), so a client that gives up does not leave a
permit consumed.

The permit bound is the *only* numeric cap the product places on generation. ADR 0002
records the deliberate absence of the others: no cap on source dimensions, pixel counts,
file length, decoded or encoded bytes, or CPU and wall-clock time, because capping
Jellyfin's own generated data would reject valid input the server produced. What follows
from that is stated honestly there — abnormal trusted input can exhaust managed or native
resources, and that exhaustion is outside the recovery guarantee. The design accepts it
rather than refuse data the server considers good. The bound covers host CPU as well as
disk: it is a statement about the whole cost of being a tenant in the server process, not
only about the bytes written to the tree.

## Where it is enforced

[Scheduled cleanup](../lifecycle/scheduled-cleanup.md), which draws the run: cutoff,
discovery, classification, re-check, deletion, pruning. The decode permit bound is
enforced in [preview generation](../lifecycle/preview-generation.md), as the first step
before a sprite is opened.

## How a caller observes it

Nobody can. This is the one promise with no caller-visible signal: a client sees only an
occasional cache miss where a hit might have been, and cannot distinguish that from a
first request. It is observable by an administrator, in the run's report and in the size
of the tree over time.
