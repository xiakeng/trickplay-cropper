# C# Coding Standard Adoption Profile

Use this profile for production code, tests, tools, and build code in any C#
repository that adopts these files. Generated code is exempt when its generator
cannot produce compliant output.

## Baseline

The normative baseline is the vendored CSharpGuidelines Agent Skill at
`docs/agents/csharp-guidelines/SKILL.md`. Its upstream version and license are
recorded in `docs/agents/csharp-guidelines/UPSTREAM.md`.

Before writing or refactoring C# code:

1. Read the Skill entry point.
2. Read every referenced category affected by the change.
3. Read `docs/agents/csharp-guidelines-overrides.md` when it exists.
4. Inspect the repository's compiler, analyzer, formatter, and test settings.

This step is complete when every changed C# concern is covered by an upstream
category, a repository override, or an explicit project contract.

## Authority and precedence

Resolve conflicts in this order:

1. The issue, approved specification, and observable behavior contract.
2. Repository architecture, domain, compatibility, and public API decisions.
3. Compiler, `.editorconfig`, analyzer, formatter, and build configuration.
4. Documented repository overrides.
5. The vendored CSharpGuidelines baseline.
6. Established local patterns where the preceding sources are silent.

Do not silently choose between conflicting authorities. Preserve the higher
authority and record a durable deviation in the repository overrides file.

## Applying the baseline

Apply the Skill's severity vocabulary consistently:

- **Must** rules are mandatory unless a higher authority explicitly overrides
  them.
- **Should** rules are the default; deviate only for a concrete engineering
  reason that is visible in the change or repository documentation.
- **May** rules are optional and must not become review blockers by preference
  alone.

Use rule identifiers when documenting exceptions or review findings. Avoid
copying upstream rule text into local documents; link to the vendored rule so
the baseline remains the single source of truth.

## Automated enforcement

Express deterministic decisions through `.editorconfig`, compiler options,
analyzers, formatters, or build targets. Keep those settings aligned with the
adopted baseline and documented overrides.

An automated diagnostic is authoritative for the rule it implements. Suppress
one only at the narrowest practical scope and include a concrete justification.
Do not add an abandoned analyzer solely to claim broader guideline coverage.

## Completion criteria

A C# change is complete when:

- all modified files satisfy the configured formatter and analyzers;
- the production-equivalent build completes without new warnings;
- relevant tests pass at the appropriate seams;
- every skipped or unavailable check is reported as residual risk; and
- the final report names the checks that actually ran.

## Updating the baseline

Update the vendored Skill only in an issue-backed change. Pin a released tag and
commit, preserve its license and provenance, review the upstream release notes,
reconcile local overrides and executable configuration, and verify that every
vendored upstream file is either unchanged or explicitly documented as adapted.
