# CSharpGuidelines Repository Overrides

Automated formatting, naming, compiler, and analyzer diagnostics take
precedence over subjective review comments about the same rule.

Keep documentation synchronized with the code. When a change modifies behavior that
`README.md` defines, or restructures code that a
[Code Map](../code-maps/README.md) describes, update that documentation in the same
change. Documentation that no longer matches the code is a defect of the change, not
follow-up work.

`docs/business/` is maintained asynchronously through the open `docs:business-analysis`
issue the merge workflow maintains after merges to `main`; the
[Business Documentation index](../business/README.md) records the base and the rules.
Deferred analysis is not an exemption from documentation work — implementation changes
still own the root README and the affected Code Maps in the same pull request.

## Repository size limits

Every Markdown source in the [Code Maps](../code-maps/README.md) collection — the index
and any future nested map — is limited to 1,000 `cl100k_base` tokens over its complete
source, counted with tiktoken 0.12.0. The limit applies nowhere else in the repository.
