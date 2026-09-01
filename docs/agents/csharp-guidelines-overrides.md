# CSharpGuidelines Repository Overrides

Apply these repository decisions after the vendored CSharpGuidelines baseline.
Keep this file limited to explicit deviations and clarifications; domain and
feature requirements belong in their own architecture, domain, and specification
documents.

## Deviations

- **AV1702 and AV1705:** Use `_camelCase` for private instance fields and
  `s_camelCase` for private static fields. Constants remain `PascalCase`.
  `.editorconfig` is the executable authority.
- **AV1602:** Suffix test classes with `Tests`, matching the established test
  suite.
- **AV1500:** Treat fifteen statements as a review signal rather than a hard
  numerical limit. Extract code when doing so improves cohesion, naming,
  abstraction level, ownership, or testability.
- **AV1150:** Local functions are allowed when they improve locality and do not
  hide reusable behavior.
- **AV2402:** In namespaced C# files, place `using` directives inside the
  namespace, as configured by `.editorconfig`.

## Clarifications

- **AV1755:** Suffix project-owned task-returning asynchronous methods with
  `Async`. Preserve framework, interface, and override signatures.
- Automated formatting, naming, compiler, and analyzer diagnostics take
  precedence over subjective review comments about the same rule.
