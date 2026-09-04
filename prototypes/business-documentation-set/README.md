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

## What measuring the diagrams settled

A rule that was not obvious before drafting, and that constrains every future view
in this set: **GitHub scales a Mermaid diagram to fit the reading column, so a
wide, flat diagram is scaled down until its text is unreadable, while a narrow,
tall one is drawn at full size and simply scrolls.**

The first draft got this wrong three times over. Laying a linear pipeline out left
to right looked tidier on paper and measured 2823 × 366 — drawn in a column about
1000 px wide, that is a third of its natural size. Fanning ten identity inputs into
one node measured 2473 × 558 for the same reason. Both were fixed by going
top-down, and the fan was fixed again by grouping ten inputs into four labelled
clusters.

A second, opposite mistake: collapsing ten refusal terminals into one shared node
made the source-resolution view *taller*, 2578 → 2857, because a node with
incoming edges from many ranks is placed below all of them, and every early exit
stretches into a long edge. Splitting one deep view into two shallow ones, gates
and then selection, is what actually shortened it.

The rule the set now follows:

- Top-down by default. Left-to-right only for a chain short enough to stay inside
  the column.
- No rank wider than about four nodes; group inputs instead of fanning them.
- No view deeper than about eight ranks; split the chapter's halves into two views
  instead.
- Do not share one terminal node across many ranks. Give each rank its own short
  exit, and let a table carry the detail.

Measured result after those fixes, as intrinsic SVG size and the scale GitHub would
apply in a 1000 px column: every one of the twelve views renders, none below 83 %.

| View | Size | Scale |
|---|---|---|
| `README.md` lifecycle | 897 × 1192 | 100 % |
| `source-resolution.md` gates | 848 × 1645 | 100 % |
| `source-resolution.md` selection | 671 × 1210 | 100 % |
| `frame-probe.md` truncated pipeline | 1129 × 700 | 89 % |
| `frame-selection.md` derivation | 679 × 950 | 100 % |
| `frame-selection.md` sprite grid | 762 × 414 | 100 % |
| `preview-generation.md` generation | 567 × 1414 | 100 % |
| `preview-cache.md` identity convergence | 1206 × 630 | 83 % |
| `cache-coordination.md` two callers | 1072 × 1218 | 93 % |
| `cache-coordination.md` publication race | 799 × 1170 | 100 % |
| `client-interaction.md` conversation | 739 × 853 | 100 % |
| `scheduled-cleanup.md` the run | 825 × 1841 | 100 % |

## How to view

Pushed to the branch `prototype/business-documentation-set`. Read the files on
github.com, which renders Mermaid inline; no build, no server.

What was actually verified here: all twelve views were parsed *and* rendered to SVG
by Mermaid 11 running locally, with zero parse and zero render errors, and the
sizes in the table above are the measured intrinsic SVG dimensions. GitHub's own
renderer was not confirmed — it delegates to `viewscreen.githubusercontent.com` in
a cross-origin iframe, and the embedded browser available for this check never
completes that round-trip, leaving the raw-source fallback on screen. That is a
limitation of the checking environment, not evidence of a problem with the diagrams,
but it is unverified, so worth one look in a real browser.
