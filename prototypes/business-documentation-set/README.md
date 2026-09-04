# PROTOTYPE — business documentation set (throwaway)

Prototypes the answer to [Prototype the business-logic documentation set](https://github.com/xiakeng/trickplay-cropper/issues/54),
a ticket on the map [Wayfinder: Specify adaptive previews, automated distribution, and live verification](https://github.com/xiakeng/trickplay-cropper/issues/43).

**Nothing here is documentation of the product.** This directory is a throwaway
artifact for choosing a structure. The validated decision folds into the real
`docs/business/` tree later; this directory never merges to `main`.

## The question

What concrete multi-file `docs/business/` outline, and which concise Mermaid
views, best explain the business logic of Trickplay Cropper — from the
top-level lifecycle down through configured source resolution, the client
HEAD/cache/GET interaction, Frame Selection, Preview generation, Cache Tree
coordination, entry-lock acquisition and release, and scheduled cleanup — while
excluding tests, GitHub Actions, release mechanics, and other development
operations?

Two sub-questions are entangled, which is why three variants are on offer:

1. **How is the set split into files?** By the order a request flows, by the
   participant responsible, or by the promise being kept.
2. **Which view does each file carry?** A Mermaid diagram earns its place only
   if it shows something the prose cannot: a branchy decision path, an ordering
   constraint between parties, or a race with more than one legal outcome.

## Assumptions baked into every variant

- **The set describes decided behavior, not today's `main`.** The Trickplay
  Frame Probe and the Selected Trickplay Resolution are specified on the map but
  not yet implemented; the current code exposes GET only and selects a fixed
  320 px width. Writing happens in the next stage, after the business logic is
  implemented, so the prose will describe real behavior. The *structure* decided
  here does not depend on that ordering.
- **Execution order for the next stage: business logic → documentation →
  integration tests.** Documentation sits between the two, so it describes
  implemented behavior and is in place before the suite runs.
- **Domain language first, light code anchors second.** Prose uses the
  `CONTEXT.md` glossary and avoids the synonyms it rejects. Each file may end
  with a short anchor line naming the types and methods that carry the rule —
  names only, never line numbers, so anchors do not rot on edit.
- **`docs/business/` does not duplicate `docs/spec/`.** The specification states
  the contract normatively for implementers; the business set explains why the
  behavior is what it is, for a reader who will not open the code.
- **README owns product introduction, features, installation, update, rollback,
  build, test, and local integration-test guidance.** The business set links
  nowhere near release mechanics.

## The three variants

| | [A — Lifecycle](outline-a-lifecycle.md) | [B — Participant](outline-b-participant.md) | [C — Guarantee](outline-c-guarantee.md) |
|---|---|---|---|
| Split by | the order a request flows through the plugin | who is responsible for what | the promise being kept |
| Spine | time | actor | invariant |
| Dominant diagram | `flowchart` down the pipeline | `sequenceDiagram` between parties | decision path plus the failure it prevents |
| Files | 9 | 7 | 8 |
| Reads well for | a newcomer following one request end to end | someone integrating a client, or debugging one party | a reviewer checking nothing is unenforced |
| Weak at | cross-cutting rules get repeated in several chapters | the internal mechanism has no single home | nobody can tell you what happens, in order |

**Recommendation: A, with C's opening move borrowed.** A lifecycle spine matches
the question's own phrasing ("from the top-level lifecycle down through…") and is
the only variant a newcomer can read straight through. Its known weakness — a
cross-cutting rule like cache identity appearing in three chapters — is fixed by
giving each A chapter C's first section: an explicit statement of the guarantees
that chapter upholds, before any mechanism. B's participant view survives as one
chapter of A (`client-interaction.md`) rather than as the whole spine, because
only the client boundary genuinely needs a sequence diagram.

Fully drafted under [`draft/`](draft/README.md) as it would land in
`docs/business/`, so the depth, the diagram style, and the anchor lines can be
judged on real prose rather than on a promise about it.

## How to view

Pushed to the branch `prototype/business-documentation-set`. GitHub renders the
Mermaid blocks inline, so read the files on github.com; no build, no server.
