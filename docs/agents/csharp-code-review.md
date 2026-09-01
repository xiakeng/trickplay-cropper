# C# Code Review Contract

Use this contract for reviews of C# production code, tests, tools, and build
configuration. Read the governing issue or specification, `CONTEXT.md`, relevant
ADRs, and `docs/agents/csharp-coding-standard.md` before evaluating the diff.

## Review sequence

### 1. Establish the contract

- Identify the requested behavior, explicit non-goals, compatibility promises,
  and acceptance criteria.
- Determine the review base and inspect the complete diff, including tests,
  configuration, packaging, and documentation.
- Inspect neighboring code and call sites before treating a local pattern as a
  defect.

This step is complete when every changed behavior is mapped to an acceptance
criterion or identified as unintended scope.

### 2. Review behavior and design

Check, in order:

1. Correctness on success, empty, boundary, malformed, and failure paths.
2. Authorization and media-source membership before Source Sprite access.
3. Public API, plugin identity, package, and Jellyfin compatibility.
4. Preview Identity, frame selection, crop bounds, cache validators, and HTTP
   semantics.
5. Cancellation, exception translation, logging context, and disposal.
6. Preview Cache Entry races, Cache Tree leases, atomic publication, cleanup
   revalidation, and reparse-point safety.
7. Allocation, scanline decode behavior, concurrency bounds, and evidence for
   performance claims.
8. Tests that independently prove the changed behavior at the correct seam.

This step is complete when every changed execution path and shared-state
transition has been accounted for.

### 3. Review maintainability

- Confirm names use the glossary and make ownership and units clear.
- Confirm abstractions own real policy and dependencies point in the intended
  direction.
- Look for duplicated policy, hidden side effects, mixed abstraction levels,
  and code that is difficult to test or diagnose.
- Leave deterministic formatting, naming, and analyzer findings to the build
  unless the configuration itself is incorrect.

This step is complete when every non-automated standard relevant to the diff has
been considered.

## Finding bar

Report a finding only when the diff introduces or exposes an actionable problem
with a concrete failure scenario or engineering impact. Each finding must:

- identify one precise file and the smallest useful line range;
- state the conditions that trigger the problem;
- explain the observable impact;
- cite the violated contract, ADR, analyzer, or project-standard section; and
- describe a minimal direction for remediation without rewriting the patch.

Avoid findings that are purely subjective, already produced by configured
tooling, unrelated to the changed code, or unsupported by a plausible scenario.
Do not inflate severity because a rule uses imperative wording.

## Severity

- **P0 — Critical:** active security compromise, unrecoverable data loss, or a
  service-wide outage. Stop delivery.
- **P1 — High:** authorization bypass, common-path incorrect output or crash,
  broken public/package compatibility, or a likely server-wide availability
  failure. Block the pull request.
- **P2 — Medium:** a reproducible edge-case bug, race, resource leak, unsafe
  filesystem behavior, or missing verification that permits a concrete
  regression. Block until resolved or explicitly accepted.
- **P3 — Low:** a narrow but concrete maintainability, diagnostics, or future
  correctness risk. Normally non-blocking.

## Verification and output

Use tests or a focused reproduction when they can confirm or disprove a suspected
finding. Run the configured formatter, Release build, and relevant tests when
the review environment permits it. Report only checks that actually ran.

Present findings first, ordered by severity and then file location. Use this
shape:

```text
[P1] Short actionable title
file:line
Trigger: ...
Impact: ...
Evidence: ...
Remediation: ...
```

If there are no actionable findings, say so explicitly. Follow with residual
risks or checks that could not be performed; do not invent a style finding to
fill the response.

## Upstream basis

This contract adapts the CC BY 4.0 licensed
[Microsoft Engineering Fundamentals reviewer guidance](https://microsoft.github.io/code-with-engineering-playbook/code-reviews/process-guidance/reviewer-guidance/)
and [C# review checklist](https://microsoft.github.io/code-with-engineering-playbook/code-reviews/recipes/csharp/),
plus the MIT-licensed verification principles in the
[.NET Runtime agent instructions](https://github.com/dotnet/runtime/blob/main/.github/copilot-instructions.md).
Repository-specific requirements in this document are authoritative here;
upstream changes are not adopted automatically.
