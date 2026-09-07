# CSharpGuidelines Repository Overrides

Automated formatting, naming, compiler, and analyzer diagnostics take
precedence over subjective review comments about the same rule.

Keep documentation synchronized with the code. When a change restructures code that a
[Code Map](../code-maps/README.md) describes, update the affected maps in the same
change. Documentation that no longer matches the code is a defect of the change, not
follow-up work.

Keep Code Maps and Business Documentation as independent surfaces. Derive every Code
Map claim from the current code, tests, workflows, and repository structure. Code Maps
must not link to, restate, or rely on `docs/business/`, because Business Documentation
is maintained on a deferred schedule and may describe an older implementation. Do not
add cross-links between `docs/code-maps/` and `docs/business/`.

## Unit tests guidelines
Follow guidelines in `docs/agents/test-value-gate.md`

## Repository size limits

Every Git-tracked, hand-written production-code, test, and script file is limited to
500 physical lines. Blank lines and comments count. LF and CRLF each terminate one
line, a final unterminated nonempty line counts, a terminal newline adds no phantom
line, and an empty file has zero lines. Generated and third-party material are the
only permitted exclusions, and every exclusion must name the precise generated or
third-party target rather than exempting a directory that can contain hand-written
repository code.

Every Markdown source in the [Code Maps](../code-maps/README.md) collection — the index
and any future nested map — is limited to 1,000 `cl100k_base` tokens over its complete
source, counted with tiktoken 0.12.0. The limit applies nowhere else in the repository.
