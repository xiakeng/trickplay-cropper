# Probe isolation

## The promise

Asking which frame a position selects never costs image work, and never promises that
the frame can be delivered.

## What breaks without it

**The cheap question becomes the expensive answer.** A client scrubbing a timeline
asks about a position on every movement — many times a second. If each probe resolved
a sprite, statted a file, or looked in the cache, dragging across an hour of video
would generate a burst of filesystem and cache work for frames nobody will look at.
The client would be penalized for asking a question that has an arithmetic answer.

**A cold cache would make probes slow.** A probe that touches the Cache Tree inherits
the tree's contention: it waits behind locks, and its latency becomes a function of
what other callers are doing. The one operation that should be predictable stops
being predictable.

**A probe would create the work it reports on.** Worst case, a probe that checks
whether a preview exists triggers its generation. Asking about a frame would become
the reason the frame was made, multiplied by every position scrubbed past.

**A client would trust the wrong thing.** If a probe implied deliverability, a `404`
from the following preview request would be inexplicable, and clients would grow retry
logic around a distinction the product never made.

## Why this shape

**The isolation is structural, not disciplined.** The probe's path has no route to a
current-user resolver, a user-scoped Item lookup, playback authorization, a sprite,
the Cache Tree, or the encoder — not routes it declines to take. A guarantee that
depends on a code path choosing not to call something is a guarantee that the next
change can silently break; one that depends on there being nothing to call is not.
The probe and preview share only the resolution and Frame Index calculation, rather
than sharing a request context or turning preview behavior off with flags.

**The rejected alternative was the framework default.** An HTTP framework will
typically answer HEAD by running the GET handler and discarding the body. That is
exactly what this design refuses: it would make a probe cost a full generation, and
would return every header a GET returns, including an ETag. The probe is a distinct
operation with a distinct path.

**No ETag, because an ETag describes bytes.** The probe has deliberately not looked
at any. Returning one would claim knowledge the operation refuses to acquire, and a
caller would reasonably treat it as a claim about the image — caching against it,
comparing it, trusting it. The two headers it does return identify the frame and
nothing about the image.

**No conditional behaviour, for the same reason.** Honouring `If-None-Match` on a
probe would mean comparing something the probe never obtained. A probe answers
freshly every time, which is also what makes it safe to ask repeatedly.

**The non-promise is stated, not left to be discovered.** "A successful probe is not
proof a preview can be served" is uncomfortable to document and worse to omit. The
sprite availability gate exists on the preview path because recorded metadata does not
prove a file exists — see
[Jellyfin Server](../participants/jellyfin-server.md) — and the probe stops before it.
Pretending otherwise would produce a product whose documented behaviour clients could
not rely on.

## Where it is enforced

[Trickplay Frame Probe](../lifecycle/frame-probe.md), which shows the pipeline
truncated and the unreachable remainder.

## How a caller observes it

An empty body and exactly two plugin-owned headers, whatever else is happening on the
server. A probe never touches the Cache Tree or image work, whether the cache is cold or
the disk is busy. Its full Media Source enumeration, dynamic providers, and generated
metadata read can still perform I/O and affect latency; retained calculation caching is
separate follow-up work. The answer establishes calculation availability, not user
visibility, playback permission, or preview deliverability.
