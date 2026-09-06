# CSharpGuidelines Repository Overrides

Automated formatting, naming, compiler, and analyzer diagnostics take
precedence over subjective review comments about the same rule.

Keep documentation synchronized with the code. When a change modifies behavior that
`README.md` defines, or restructures code that a
[Code Map](../code-maps/README.md) describes, update that documentation in the same
change. Documentation that no longer matches the code is a defect of the change, not
follow-up work.

`docs/business/` is maintained asynchronously. After a pull request merges into `main`,
the merge workflow keeps at most one open `docs:business-analysis` issue directing an
analysis pass from the base recorded in the Business Documentation index. Deferred
analysis is not an exemption from documentation work — implementation changes still
own the root README and the affected Code Maps in the same pull request.
