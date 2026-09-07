# C# Code Review Contract

Use this contract for C# production code, tests, tools, and build configuration.

## Prepare

1. Identify the review base and inspect the complete diff.  
   Do not increase the diff context by tweak --unified.  
   If lack context to do proper review, search and read required contents only.
2. Read the governing issue or specification.
3. Apply `docs/agents/csharp-guidelines/SKILL.md`, every reference category
   touched by the change, and `docs/agents/csharp-guidelines-overrides.md`.   
   Do not bulk-read all references files, read only matching sections.
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
3. Do not review unchanged hunks from before the checkpoint.
4. After the required review axes pass, advance the checkpoint to the current
   `HEAD`.

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
  map to be updated in the same change。
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
- After non-mechanical review fixes, review the final HEAD again.

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

Present findings first, ordered by file location. For each finding,
report its title, location, trigger, impact, evidence, and remediation. If there
are no actionable findings, say so explicitly.

## Verification

Run the configured formatter, production-equivalent build, and relevant tests
when possible. Report only checks that ran, followed by residual risks and
unperformed checks.
