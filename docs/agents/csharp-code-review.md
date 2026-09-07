# C# Code Review Contract

Use this contract for C# production code, tests, tools, and build configuration.

## Reviewer handoff

The main agent must read this contract before dispatching reviewers. For each
review invocation, create a fresh reviewer for each required axis without
inheriting implementation or previous-review conversation history (for example,
`fork_turns="none"` where supported). Keep the checkpoint and outstanding findings
in the main agent's review record; do not reuse a reviewer session for re-review.

Give each reviewer a compact handoff containing the repository path, review axis,
fixed base and head SHAs, governing issue/specification link, relevant requirements,
non-goals and explicit exceptions with their source, and paths to this contract
and applicable repository instructions. Include the validation summary described
below. For re-review, also include outstanding finding IDs, locations, triggers,
and fix dispositions so unresolved findings survive the fresh context.

Keep full diffs and logs in the repository or local artifacts; reviewers read the
specified diff and relevant excerpts on demand. Batch independent reads when
practical, and request additional context only to resolve a concrete review question.

## Prepare

1. Inspect the complete diff within the supplied base/head scope using default
   diff context. Do not expand it with `--unified`; read targeted source ranges
   when a specific question needs more context.
2. Read the governing issue or specification's relevant requirements, non-goals,
   and explicit exceptions. Resolve missing or ambiguous details at the source.
3. Apply `docs/agents/csharp-guidelines/SKILL.md`, every reference category
   touched by the change, and `docs/agents/csharp-guidelines-overrides.md`.
   Read only matching reference sections, not all reference files in bulk.
4. Inspect affected call sites, configuration, and tests.
5. Identify the documentation that describes the changed behavior: the affected Code
   Maps under `docs/code-maps/`.

Preparation is complete when every changed behavior maps to the requested
contract or is identified as unintended scope.

## Incremental /code-review rule

Within one `/implementation` task, the first `/code-review` reviews the complete
branch diff against its original fixed point and runs both Standards and Spec.

After that review, record the reviewed `HEAD` commit as the review checkpoint.
For every subsequent review:

1. Review only `git diff <last-reviewed-head>..HEAD` and its commits.
2. Review only the affected axis:
   - Run Standards for changes made solely to resolve Standards findings.
   - Run Spec for changes made solely to resolve Spec findings or acceptance
     requirements.
   - Run both only when the new changes affect both axes.
3. Do not re-review unchanged hunks from before the checkpoint. Read unchanged
   code only as context for the new diff or to verify an outstanding finding.
4. After the required review axes pass, advance the checkpoint to the current
   `HEAD`. Retain unresolved findings until verified as fixed or explicitly
   dispositioned; an empty incremental diff does not clear them.

This incremental-review rule overrides `/code-review`'s default full-diff,
two-axis behavior for repeated reviews within the same implementation task.
If the checkpoint is missing, is not an ancestor of `HEAD`, or the review scope
is uncertain, perform the complete two-axis review again.

## Review

- Verify success, boundary, malformed-input, cancellation, and failure paths
  relevant to the contract.
- Check compatibility, public behavior, state transitions, resource ownership,
  concurrency, exceptions, and performance where affected.
- Verify the code still matches the affected Code Maps under `docs/code-maps/`. When
  the change restructures code a Code Map describes, flag the mismatch and require the
  map to be updated in the same change.
- Apply every relevant CSharpGuidelines rule, leaving deterministic diagnostics
  to configured tooling unless the configuration was bypassed or is incorrect.
- Confirm tests independently prove the changed behavior at stable seams.
- Unit tests should follow guidelines under `docs/agents/test-value-gate.md`
- Prefer the simplest design that fully satisfies the requirement.
- Keep one authoritative source for each fact; derive copies, fixtures, and
  expectations from it.
- Avoid duplication, speculative abstractions, hidden side effects, and
  unnecessary dependencies.
- Make invalid states, boundaries, and failure paths explicit.
- Use names that reveal intent. Comments explain why, not what.
- Preserve existing behavior unless the specification explicitly changes it.
- Review the final diff for design, correctness, simplicity, tests, naming,
  comments, style, and documentation.
- After non-mechanical review fixes, review the final HEAD using the incremental
  rule above.

Review is complete when every changed execution path and applicable guideline
category has been considered.

## Findings

Report only actionable problems with a concrete trigger and engineering impact.
Each finding must identify the smallest useful file and line range, explain the
trigger and impact, cite the governing contract or rule, and give a minimal
remediation direction.

Exclude subjective preferences, diagnostics already emitted by configured
tooling, unrelated pre-existing problems, and concerns without a plausible
failure scenario.

Standards findings must respect the issue's explicit exceptions and non-goals.
Exclude recommendations that require out-of-scope work or violate an applicable
exception. Silence in the issue is not a waiver of repository standards. Continue
to report correctness and safety defects caused or exposed by the change; cite
an unresolved contract conflict rather than inventing an exception.

Present findings first, ordered by file location. For each finding,
report its title, location, trigger, impact, evidence, and remediation. If there
are no actionable findings, say so explicitly.

## Verification

The main agent owns routine formatting, production-equivalent build, and relevant
test verification. Share one compact evidence summary for the reviewed head:
commit SHA, working-tree state, each command and relevant configuration, exit
code, one-line result, and log path. Keep full console output in local artifacts.
Evidence must identify the tested state; commit SHA alone is insufficient if
uncommitted changes were present or the working tree changed after verification.

Reviewers reuse successful evidence for the same code state. Supplement it only
when evidence is missing, stale, insufficient for the reviewed behavior, or a
specific finding needs verification. State the gap or hypothesis first and run
the smallest relevant check; read only relevant log excerpts. Report reused
checks separately from checks run by the reviewer, followed by residual risks
and unperformed checks.
