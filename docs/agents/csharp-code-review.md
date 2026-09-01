# C# Code Review Contract

Use this contract to review C# production code, tests, tools, and build
configuration in any repository that adopts these files.

## Establish the contract

Before reviewing the implementation:

1. Identify the review base and inspect the complete diff.
2. Read the governing issue or specification, including explicit non-goals and
   compatibility promises.
3. Read `docs/agents/csharp-guidelines/SKILL.md` and every upstream reference
   category touched by the change.
4. Read `docs/agents/csharp-coding-standard.md` and
   `docs/agents/csharp-guidelines-overrides.md` when it exists.
5. Inspect affected call sites, configuration, and neighboring code.

This step is complete when every changed behavior maps to the requested contract
or is identified as unintended scope.

## Review axes

Review the change along both axes. Passing one does not compensate for failing
the other.

### Specification

- Verify success, empty, boundary, malformed-input, cancellation, and failure
  paths that the contract makes relevant.
- Preserve authorization, compatibility, serialization, persistence, packaging,
  and public API behavior unless the contract changes them.
- Account for state transitions, retries, partial failures, and shared-resource
  ownership.
- Confirm tests independently prove the requested behavior at stable seams.

### Standards

- Apply every relevant Must and Should rule from the vendored Skill, subject to
  the adoption profile and documented overrides.
- Check nullability, exception boundaries, disposal, asynchronous control flow,
  cancellation propagation, concurrency, and allocation behavior where relevant.
- Confirm types and methods remain cohesive, dependencies and ownership are
  explicit, and abstractions own real policy.
- Leave deterministic formatting and analyzer findings to configured tooling
  unless the configuration itself is incorrect or was bypassed.

This step is complete when every changed execution path, shared-state transition,
and applicable guideline category has been considered.

## Finding bar

Report a finding only when the diff introduces or exposes an actionable problem
with a concrete failure scenario or engineering impact. Each finding must:

- identify one precise file and the smallest useful line range;
- state the conditions that trigger the problem;
- explain the observable impact;
- cite the violated specification, guideline rule, analyzer, or contract; and
- describe a minimal remediation direction without rewriting the patch.

Do not report purely subjective preferences, diagnostics already produced by
configured tooling, unrelated pre-existing problems, or concerns without a
plausible trigger and impact.

## Severity

- **P0 — Critical:** active security compromise, unrecoverable data loss, or a
  service-wide outage. Stop delivery.
- **P1 — High:** authorization bypass, common-path incorrect behavior or crash,
  broken compatibility, or a likely system-wide availability failure. Block the
  change.
- **P2 — Medium:** a reproducible edge-case defect, race, resource leak, unsafe
  boundary behavior, or missing verification that permits a concrete regression.
  Block until resolved or explicitly accepted.
- **P3 — Low:** a narrow but concrete maintainability, diagnostics, or future
  correctness risk. Normally non-blocking.

## Verification and output

Use tests or a focused reproduction when they can confirm or disprove a suspected
finding. Run the configured formatter, production-equivalent build, and relevant
tests when the review environment permits it. Report only checks that actually
ran.

Present findings first, ordered by severity and then file location:

```text
[P1] Short actionable title
file:line
Trigger: ...
Impact: ...
Evidence: ...
Remediation: ...
```

If there are no actionable findings, say so explicitly. Follow with residual
risks and checks that could not be performed; do not invent a finding to fill the
response.

## Upstream basis

This generic review contract is informed by the CC BY 4.0 licensed
[Microsoft Engineering Fundamentals reviewer guidance](https://microsoft.github.io/code-with-engineering-playbook/code-reviews/process-guidance/reviewer-guidance/)
and [C# review checklist](https://microsoft.github.io/code-with-engineering-playbook/code-reviews/recipes/csharp/),
plus the MIT-licensed verification principles in the
[.NET Runtime agent instructions](https://github.com/dotnet/runtime/blob/main/.github/copilot-instructions.md).
