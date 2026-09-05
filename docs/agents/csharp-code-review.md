# C# Code Review Contract

Use this contract for C# production code, tests, tools, and build configuration.

## Prepare

1. Identify the review base and inspect the complete diff.
2. Read the governing issue or specification.
3. Apply `docs/agents/csharp-guidelines/SKILL.md`, every reference category
   touched by the change, and `docs/agents/csharp-guidelines-overrides.md`.
4. Inspect affected call sites, configuration, and tests.
5. Identify the documentation that describes the changed behavior:
   `docs/business/` and `README.md`.

Preparation is complete when every changed behavior maps to the requested
contract or is identified as unintended scope.

## Review

- Verify success, boundary, malformed-input, cancellation, and failure paths
  relevant to the contract.
- Check compatibility, public behavior, state transitions, resource ownership,
  concurrency, exceptions, and performance where affected.
- Verify the code still matches `docs/business/` and `README.md`. When the
  change alters behavior those documents define — headers, statuses,
  ownership, ordering, bounds, configuration, commands, or outputs — flag the
  mismatch and require the documentation to be updated in the same change; a
  follow-up ticket is not an acceptable remediation.
- Apply every relevant CSharpGuidelines rule, leaving deterministic diagnostics
  to configured tooling unless the configuration was bypassed or is incorrect.
- Confirm tests independently prove the changed behavior at stable seams.
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

Severity:

- **P0:** active compromise, unrecoverable data loss, or system-wide outage.
- **P1:** authorization bypass, common-path failure, broken compatibility, or
  likely system-wide unavailability.
- **P2:** reproducible edge-case defect, race, leak, unsafe boundary behavior, or
  a concrete regression left unverified.
- **P3:** narrow but concrete maintainability, diagnostics, or future correctness
  risk.

Present findings first, ordered by severity and file location. For each finding,
report its title, location, trigger, impact, evidence, and remediation. If there
are no actionable findings, say so explicitly.

## Verification

Run the configured formatter, production-equivalent build, and relevant tests
when possible. Report only checks that ran, followed by residual risks and
unperformed checks.
