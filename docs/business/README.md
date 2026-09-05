# Business documentation

Trickplay Cropper is a Jellyfin server plugin that serves one JPEG frame of a video for an
authorized playback position, cropped from trickplay data that Jellyfin already generated.
It never generates, modifies, or repairs that data.

These chapters explain the business logic: what the product does, why it does it that way,
and who is allowed to change what. They do not cover tests, build, packaging, GitHub
Actions, Release publication, or the plugin manifest — those are development operations.
Installation, update, and rollback guidance lives in the
[repository README](../../README.md).

## Three layers, in reading order

The same product is described three times from three angles, because the three questions
readers actually arrive with are different questions. Read them in this order the first
time.

### 1. [Participants](participants/README.md) — how the whole thing runs

Who is in the room, what each one owns, and what crosses the boundaries. The shortest
layer, and the only one that says who may change what — which is the fact the other two
keep depending on. Start here.

*No mechanism. Every "how" is a link outward.*

### 2. [Lifecycle](lifecycle/README.md) — what actually happens

What happens, in order, when a request arrives: the authorization gates, the selection of
one exact resolution, Frame Selection, the probe's stopping point, generation, the cache
and its coordination, the response contract, and the cleanup run.

*The only layer that describes mechanism. If you want to know how something works, this is
where the answer lives.*

### 3. [Design](design/README.md) — why it is shaped this way

What the product promises, what breaks without each promise, which alternatives were
rejected and why, and what it deliberately does not promise at all.

*Never restates a mechanism. Read it once you know what happens, and it answers "why not
something else".*

## Which layer do I need?

| You want to know… | Read |
|---|---|
| What the plugin is allowed to touch, and what Jellyfin may change under it | [Participants](participants/README.md) |
| What a client may rely on, and what it must decide for itself | [The client](participants/client.md) |
| What happens when a request arrives, step by step | [Lifecycle](lifecycle/README.md) |
| Which headers and statuses an operation can produce | [The response contract](lifecycle/response-contract.md) |
| Where a frame is cached, and what makes two previews the same | [Preview Cache Entry](lifecycle/preview-cache.md) |
| Why there is no nearest-resolution fallback | [Resolution exactness](design/resolution-exactness.md) |
| Why a successful probe can be followed by a `404` | [Probe isolation](design/probe-isolation.md) |
| Why the cache is not guarded by one lock | [Concurrency safety](design/concurrency-safety.md) |
| What the product refuses to promise | [Design, deliberate non-promises](design/README.md) |

## One rule for whoever edits this

**Each layer has one job, and no rule is stated twice.** Participants owns boundaries,
lifecycle owns mechanism, design owns rationale. When a chapter needs a fact that belongs
to another layer, it links instead of restating — because three copies of one rule become
three different rules the first time someone edits only one of them.

If you find yourself explaining *why* in the lifecycle layer, or *how* in the design layer,
the sentence belongs in the other file.

Two structural rules follow from the layers' jobs:

- Only lifecycle chapters end with an **anchor** naming the types and methods that carry
  their mechanism — names only, never line numbers, so wayfinding survives routine edits.
  Participants and design chapters carry no anchors, because they make no claim about code
  structure.
- **Diagrams stay narrow and tall.** GitHub scales a Mermaid diagram down until it fits the
  reading column, so a wide, flat diagram shrinks until its text is unreadable, while a
  narrow, tall one is drawn at full size and simply scrolls. Every diagram in this set is
  therefore top-down by default, keeps each rank to about four nodes and the whole view to
  about eight ranks, splits an oversized view in two instead of stretching it, and never
  shares one terminal node across many ranks.

## Language

The terms used here — Trickplay Preview, Trickplay Frame Probe, Source Sprite, Trickplay
Resolution Target, Selected Trickplay Resolution, Frame Index, Preview Cache Entry, Cache
Tree — are defined in the repository's [CONTEXT.md](../../CONTEXT.md), together with the
synonyms this documentation deliberately avoids.
