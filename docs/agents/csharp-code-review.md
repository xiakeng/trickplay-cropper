# C# Code Review Contract

Use this contract for C# production code, tests, tools, and build configuration.

## Prepare

1. Read the governing issue or specification's relevant requirements, non-goals,
   and explicit exceptions. Resolve missing or ambiguous details at the source.
2. Apply `docs/agents/csharp-guidelines.md`.
3. Inspect affected call sites, configuration, and tests.

Preparation is complete when every changed behavior maps to the requested
contract or is identified as unintended scope.

## Review

- Verify success, boundary, malformed-input, cancellation, and failure paths
  relevant to the contract.
- Check compatibility, public behavior, state transitions, resource ownership,
  concurrency, exceptions, and performance where affected.
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
