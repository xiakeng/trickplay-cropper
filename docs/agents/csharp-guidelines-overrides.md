# CSharpGuidelines Repository Overrides

Automated formatting, naming, compiler, and analyzer diagnostics take
precedence over subjective review comments about the same rule.

Keep documentation synchronized with the code. When a change restructures code that a
[Code Map](../code-maps/README.md) describes, update the affected maps in the same
change. Documentation that no longer matches the code is a defect of the change, not
follow-up work.

## Unit tests guidelines
Follow guidelines in `docs/agents/test-value-gate.md`

## Repository size limits

Every Markdown source in the [Code Maps](../code-maps/README.md) collection — the index
and any future nested map — is limited to 1,000 `cl100k_base` tokens over its complete
source, counted with tiktoken 0.12.0. The limit applies nowhere else in the repository.
