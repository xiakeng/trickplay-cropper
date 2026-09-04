# Why three layers

The record behind [the draft](draft/README.md). Three angles were drafted as competing
variants and then all three were kept, layered, because each answers a question the other
two answer badly.

## What each angle is good at, and what it cannot do

**By lifecycle** — one chapter per stage, in the order a request flows.

- Good at: following one request end to end without jumping files; chapter boundaries match
  the pipeline, so an implementation change lands in exactly one chapter; readable straight
  through by a newcomer.
- Cannot: tell a reviewer whether every promise is enforced, without reading all of it. Give
  an integrator just their own side. Say who is allowed to change what.

**By participant** — one chapter per party, written from that party's point of view.

- Good at: an integrator reads one chapter and stops; ownership disputes are settled
  explicitly, which is the confusion the source-trust ADR exists to prevent; sequence
  diagrams suit every chapter, so the layer is visually coherent.
- Cannot: tell you what happens, in order, for one request. Give internal mechanism a home —
  a resource like the Cache Tree is not an actor, and Frame Selection ends up split between
  two chapters, which invites divergence.

**By guarantee** — one chapter per promise, in a uniform five-section shape.

- Good at: auditability, since the index alone answers "is every promise enforced, and
  where"; the uniform shape makes an unenforced promise obvious, because an empty "how it is
  enforced" is a defect in the product rather than in the document; deliberate non-promises
  get a home, which the other two have nowhere to put.
- Cannot: narrate. There is no order, so a newcomer cannot follow one request through it,
  and the top-level lifecycle gets squeezed into the index.

None of the three is a worse document. They answer different questions, and the questions
are all real — which is why picking one was the wrong move.

## The reading order, and why it runs that way

**Participants → lifecycle → design.** Overall operation, then implementation detail, then
design mechanism.

The order follows what each layer presupposes. Ownership is the smallest and most
load-bearing fact: the lifecycle layer cannot explain why a sprite may vanish underneath a
request until you know Jellyfin owns the sprite, and the design layer cannot explain why
nothing is invalidated until you know the plugin owns only what it derived. So ownership
goes first, mechanism second, and rationale last — rationale is the layer that only makes
sense once you know what happens, and it is the layer a reviewer arrives wanting.

A reader with a specific question does not have to follow the order; the index in
[draft/README.md](draft/README.md) routes by question instead.

## The rule that makes three layers survivable

**Each layer has one job, and no rule is stated twice.**

| Layer | Owns | Never contains |
|---|---|---|
| Participants | Ownership and boundaries: who owns what, who may change what, who must never touch what | Mechanism, ordering, statuses |
| Lifecycle | Mechanism and order: what happens, when, and what it produces | Justification, rejected alternatives, what-breaks catalogues |
| Design | Promises, consequences, rationale, rejected alternatives, non-promises | Any restatement of a mechanism — always a link |

When a chapter needs a fact belonging to another layer, it links. The alternative — three
self-contained layers, each restating what it needs — reads better in isolation and fails
over time: three copies of one rule become three different rules the first time someone
edits only one of them, and a documentation set that contradicts itself is worse than none.

Two consequences worth naming, because they are where the rule bites:

- **The client conversation sequence diagram lives in participants, not lifecycle.** It
  describes an edge between two parties, which is that layer's job. The lifecycle layer
  keeps the header and status contract, which is mechanism.
- **Decode permits are a design fact, not only a mechanism.** The bound's rationale had no
  chapter until it was placed in resource bounds, which is otherwise about disk. A rationale
  with nowhere to go is how duplication starts.

## What was rejected

- **Pick one variant.** The original framing of this ticket. Rejected because the three
  angles answer three questions that all get asked, and the losing angle's readers would be
  left without a document.
- **Three self-contained layers.** Rejected on drift, as above.
- **Collapsing design into a single audit table.** Attractive — 17 files instead of 25, and
  minimal duplication — but it gives up the per-promise depth, which is the reason the layer
  exists. The audit table survives as that layer's index instead.

## The cost, stated plainly

Twenty-five files: one index, seven participants, nine lifecycle, eight design. That is a
large surface for a plugin with one controller, and it is only defensible because the layers
do not overlap: the set is long, but no sentence is repeated. A reader who follows the
reading order reads each fact once.

The risk to watch is not size, it is discipline. The rule above has to survive contact with
the next person who edits one chapter and not the other two.
