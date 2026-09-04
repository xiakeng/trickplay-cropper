# PROTOTYPE — business documentation set (throwaway)

Prototypes the answer to [Prototype the business-logic documentation set](https://github.com/xiakeng/trickplay-cropper/issues/54),
a ticket on the map [Wayfinder: Specify adaptive previews, automated distribution, and live verification](https://github.com/xiakeng/trickplay-cropper/issues/43).

**Nothing here is documentation of the product.** This directory is a throwaway artifact
for settling a structure. The validated decision folds into the real `docs/business/` tree
later; this directory never merges to `main`.

## The question

What concrete multi-file `docs/business/` outline, and which concise Mermaid views, best
explain the business logic of Trickplay Cropper — from the top-level lifecycle down through
configured source resolution, the client HEAD/cache/GET interaction, Frame Selection,
Preview generation, Cache Tree coordination, entry-lock acquisition and release, and
scheduled cleanup — while excluding tests, GitHub Actions, release mechanics, and other
development operations?

## The answer

**Three layers, not one.** Three angles were drafted as competing variants — by lifecycle,
by participant, by guarantee — and all three were kept, because each answers a question the
other two answer badly. Read in order: participants, then lifecycle, then design.

The full reasoning, including what each angle cannot do and what was rejected, is in
[layering.md](layering.md).

| Layer | Owns | Files |
|---|---|---|
| [participants](draft/participants/README.md) | Ownership and boundaries: who owns what, who may change what, who must never touch what | 7 |
| [lifecycle](draft/lifecycle/README.md) | Mechanism and order: what happens, when, and what it produces | 9 |
| [design](draft/design/README.md) | Promises, consequences, rationale, rejected alternatives, deliberate non-promises | 8 |

Plus one index: [draft/README.md](draft/README.md), which would land as
`docs/business/README.md` and carries the reading path and a route-by-question table.

**The rule that makes three layers survivable:** each layer has one job, and no rule is
stated twice. When a chapter needs a fact belonging to another layer, it links instead of
restating. The alternative — three self-contained layers — reads better in isolation and
fails over time, because three copies of one rule become three different rules the first
time someone edits only one of them.

## Assumptions baked into the draft

- **The set describes decided behavior, not today's `main`.** The Trickplay Frame Probe and
  the Selected Trickplay Resolution are specified on the map but not yet implemented; the
  current code exposes GET only and selects a fixed 320 px width. Writing happens in the
  next stage, after the business logic is implemented, so the prose will describe real
  behavior. The *structure* settled here does not depend on that ordering.
- **Execution order for the next stage: business logic → documentation → integration
  tests.** Documentation sits between the two, so it describes implemented behavior and is
  in place before the suite runs.
- **Domain language first, light code anchors second.** Prose uses the `CONTEXT.md` glossary
  and avoids the synonyms it rejects. Lifecycle chapters end with a short anchor line naming
  the types and methods that carry the rule — names only, never line numbers, so anchors do
  not rot on edit. Participants and design chapters carry no anchors, because they make no
  claim about code structure.
- **`docs/business/` does not duplicate `docs/spec/`.** The specification states the contract
  normatively for implementers; the business set explains what the product does and why, for
  a reader who will not open the code.
- **README owns product introduction, features, installation, update, rollback, build, test,
  and local integration-test guidance.** The business set links nowhere near release
  mechanics.

## What measuring the diagrams settled

A rule that was not obvious before drafting, and that constrains every view in this set:
**GitHub scales a Mermaid diagram to fit the reading column, so a wide, flat diagram is
scaled down until its text is unreadable, while a narrow, tall one is drawn at full size and
simply scrolls.**

The first draft got this wrong three times over. Laying a linear pipeline out left to right
looked tidier on paper and measured 2823 × 366 — drawn in a column about 1000 px wide, that
is a third of its natural size. Fanning ten identity inputs into one node measured
2473 × 558 for the same reason. An ownership map with three parties side by side measured
1465 × 620. All three were fixed: the pipelines went top-down, the fan was grouped into four
labelled clusters, and the parties were stacked with invisible links and joined by three
party-level edges.

A second, opposite mistake: collapsing ten refusal terminals into one shared node made the
source-resolution view *taller*, 2578 → 2857, because a node with incoming edges from many
ranks is placed below all of them, and every early exit stretches into a long edge.
Splitting one deep view into two shallow ones is what actually shortened it.

The rule the set now follows:

- Top-down by default. Left-to-right only for a chain short enough to stay inside the column.
- No rank wider than about four nodes; group inputs, or stack them with invisible links,
  instead of fanning them.
- No view deeper than about eight ranks; split the chapter's halves into two views instead.
- Do not share one terminal node across many ranks. Give each rank its own short exit, and
  let a table carry the detail.

## Verification, honestly stated

All fifteen views were parsed **and** rendered to SVG by Mermaid 11 running locally: zero
parse errors, zero render errors. The sizes below are measured intrinsic SVG dimensions from
the `viewBox`, not the on-screen box, which a container width truncates. Scale is what GitHub
would apply in a 1000 px reading column.

| View | Size | Scale |
|---|---|---|
| `participants/README.md` ownership map | 763 × 1353 | 100 % |
| `participants/cache-tree.md` nested boundary | 488 × 572 | 100 % |
| `participants/client.md` the conversation | 705 × 871 | 100 % |
| `lifecycle/README.md` the lifecycle | 897 × 1192 | 100 % |
| `lifecycle/source-resolution.md` the gates | 848 × 1645 | 100 % |
| `lifecycle/source-resolution.md` the selection | 671 × 1210 | 100 % |
| `lifecycle/frame-probe.md` truncated pipeline | 1129 × 700 | 89 % |
| `lifecycle/frame-selection.md` derivation | 679 × 950 | 100 % |
| `lifecycle/frame-selection.md` sprite grid | 762 × 414 | 100 % |
| `lifecycle/preview-generation.md` generation | 567 × 1414 | 100 % |
| `lifecycle/preview-cache.md` identity convergence | 1206 × 630 | 83 % |
| `lifecycle/cache-coordination.md` two callers | 1072 × 1218 | 93 % |
| `lifecycle/cache-coordination.md` publication race | 799 × 1170 | 100 % |
| `lifecycle/scheduled-cleanup.md` the run | 825 × 1841 | 100 % |
| `design/README.md` guarantee map | 547 × 702 | 100 % |

GitHub's own renderer was **not** confirmed. It delegates to
`viewscreen.githubusercontent.com` in a cross-origin iframe, and the embedded browser
available for this check never completes that round-trip, leaving the raw-source fallback on
screen. That is a limitation of the checking environment, not evidence of a problem with the
diagrams, but it is unverified, so worth one look in a real browser.

## How to view

Pushed to the branch `prototype/business-documentation-set`. Read the files on github.com,
which renders Mermaid inline; no build, no server. Start at
[draft/README.md](draft/README.md).
