# Coding standards

## C# design guidance

- Understand the current implementation and its callers before changing code. Do not infer behavior from names or copy patterns across unrelated contexts.
- Prefer the simplest design that fully supports the required scenarios.
- Do not add abstractions, layers, extension points, or dependencies without a concrete current need.
- Design APIs from caller scenarios. Keep common entry points discoverable, unsurprising, and self-explanatory.
- Keep responsibilities cohesive, dependencies explicit, and invariants encapsulated. Prefer composition over inheritance.
- Follow existing repository conventions and `.editorconfig`. Use modern C# when it improves clarity, correctness, or safety.
- Treat nullable annotations as contracts. Validate untrusted input at system boundaries.
- Catch exceptions only when they can be handled; otherwise preserve them. Use specific exceptions with actionable messages.
- For bugs, trace all callers and fix the shared root cause; inspect sibling occurrences of the same pattern.
- Avoid breaking public APIs. Propose usage examples and compatibility impact before changing them.
- Scale verification to the risk. Never claim an unrun build or test passed.
- Prefer self-explanatory code. Comments explain non-obvious reasons, not mechanics.

## Documentation maintenance

Content under `docs/code-maps/` and `docs/business/` is maintained only through issues labeled `doc-maintain`.

Do not proactively update, regenerate, reorganize, or otherwise maintain these directories while working on unrelated issues. If you notice stale or incorrect content, report it without modifying it.

Keep `docs/code-maps/` and `docs/business/` as independent surfaces.  
Derive every `docs/code-maps/` claim from the current code, tests, workflows, and repository structure.   
`docs/code-maps/` must not link to, restate, or rely on `docs/business/`. Do not
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
