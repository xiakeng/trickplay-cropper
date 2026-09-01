# C# Coding Standard

Apply this standard to production code, tests, and repository tools. Generated
files are exempt when their generator cannot produce compliant output.

## Authority and precedence

Resolve conflicts in this order:

1. The GitHub issue, approved specification, and observable behavior contract.
2. `CONTEXT.md`, applicable ADRs, and established public APIs.
3. `.editorconfig`, compiler diagnostics, and analyzer diagnostics for rules
   those tools can express.
4. This project standard for non-automated engineering decisions.
5. Established local patterns where the preceding sources are silent.
6. Upstream guidance not explicitly adopted by this project.

Preserve a deliberate local pattern when a nearby file differs from a general
preference. Record a recurring exception here instead of making each change
argue the same style question again.

## Correctness contract

- Keep nullable reference types enabled and model absence explicitly.
- Treat Release compiler and analyzer warnings as errors. Suppress a diagnostic
  only at the narrowest scope and include a concrete justification.
- Validate untrusted data at its boundary. Preserve trusted-input decisions in
  the specification and ADRs instead of introducing undocumented limits.
- Preserve public behavior, serialized data, plugin identity, package layout,
  and Jellyfin compatibility unless the issue explicitly changes them.
- Keep changes scoped. Separate mechanical cleanup from behavior changes when
  either change would obscure review of the other.

## Design and maintainability

- Prefer the simplest design that satisfies the current issue. Add an
  abstraction only when it owns a real policy, boundary, or variation.
- Keep each type and method cohesive. Method length and nesting are review
  signals, not numeric defects; extract code when the result improves names,
  abstraction level, testability, or ownership.
- Prefer composition and explicit dependencies over inheritance, service
  location, hidden global state, or ambient mutable state.
- Keep domain and infrastructure boundaries explicit. Cross a boundary through
  a narrow contract and use the domain terms defined in `CONTEXT.md`.
- Use local functions when they improve locality and do not hide reusable
  behavior. Move reusable policies into named members or types.

## Naming and layout

- Use `PascalCase` for namespaces, types, methods, properties, events, constants,
  and non-private fields.
- Prefix interfaces with `I`. Use `camelCase` for parameters and local variables.
- Use `_camelCase` for private instance fields and `s_camelCase` for private
  static fields. Keep constants in `PascalCase` regardless of accessibility.
- Suffix project-owned task-returning asynchronous methods with `Async`.
  Preserve framework and override signatures. Place a `CancellationToken` last
  unless a framework signature dictates otherwise.
- Prefer meaningful names over abbreviations. Use `Trickplay Preview`,
  `Source Sprite`, `Preview Cache Entry`, and `Cache Tree` exactly as defined in
  `CONTEXT.md`.
- Use file-scoped namespaces. In namespaced C# files, place `using` directives
  after the namespace declaration and sort `System` directives first.
- Use four spaces, Allman braces, and braces for every control-flow body.
- Use `var` when the assigned type is immediately evident or the explicit type
  would repeat the same information. Spell out the type when it communicates
  information not obvious from the expression.
- Follow `.editorconfig` as the executable source of formatting and naming
  decisions. Format changes should not be debated manually in code review.

## Exceptions, logging, and resources

- Throw the most specific exception that communicates the violated contract.
  Preserve the original exception as `InnerException` when adding context.
- Catch an exception only when the current boundary can recover, translate it,
  or add actionable context. Let unexpected failures propagate to the owning
  Jellyfin boundary.
- Include request, media, crop, frame, source length, and source mtime context
  when a Trickplay Preview operation fails. Exclude credentials, tokens, and
  unnecessary personal data.
- Dispose every owned `IDisposable` or `IAsyncDisposable` deterministically.
  Make ownership obvious at construction and handoff points.
- Use checked arithmetic where dimensions, offsets, lengths, or allocation sizes
  can overflow.

## Async, concurrency, and performance

- Use asynchronous APIs for asynchronous I/O. Avoid `.Result`, `.Wait()`, and
  unobserved fire-and-forget tasks.
- Accept and propagate cancellation through every cancellable operation. Do not
  translate caller cancellation into an application failure.
- Define the state protected by each lock next to its owner. Keep critical
  sections narrow and never hold a lock across unrelated I/O.
- Preserve per-entry coordination, Cache Tree leasing, atomic publication, and
  cleanup revalidation required by ADR-0001. Do not replace keyed concurrency
  with global serialization.
- Preserve the trusted Source Sprite policy and bounded decode concurrency from
  ADR-0002. Add limits only through an explicit policy decision.
- Measure performance claims on representative inputs. Prefer clear code until
  a profile or requirement identifies a hot path.

## Tests

- Add or update a test for every behavior change and bug fix. A bug fix starts
  with a test that fails for the reported behavior when practical.
- Name test classes with the `Tests` suffix. Name test methods as concise,
  present-tense descriptions of observable behavior.
- Test observable behavior through the narrowest stable API. Avoid assertions
  against private implementation details.
- Derive expected results independently from production logic. Keep important
  inputs and expected outcomes visible in the test body; move incidental setup
  into fixtures or builders.
- Use unit tests for deterministic policy and component tests for native/runtime
  integration. Add broader coverage only when the behavior crosses those seams.

## Completion criteria

A C# change is complete when all modified files conform to `.editorconfig`, the
Release solution build succeeds without warnings, relevant unit and component
tests pass, and the agent reports exactly which checks ran. An unavailable or
skipped check remains an explicit residual risk.

## Upstream basis and project decisions

This standard is informed by:

- [Microsoft common C# conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions)
  and the MIT-licensed [.NET Runtime C# style](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md).
- [CSharpGuidelines 6.0.0](https://github.com/dennisdoomen/CSharpGuidelines/releases/tag/6.0.0),
  published under CC BY-SA 4.0. Rule identifiers below are cited for
  traceability; upstream text is not incorporated verbatim.

This project deliberately overrides these opinionated CSharpGuidelines rules:

- AV1705: private fields use the .NET Runtime `_` / `s_` prefixes.
- AV1602: test classes end in `Tests`, matching the existing suite.
- AV1755: all task-returning asynchronous methods use the `Async` suffix.
- AV1500: fifteen statements is a review signal, not a hard method limit.
- AV1150: a local function is allowed when locality improves readability.

Upstream changes are not adopted automatically. Update this project standard,
`.editorconfig`, and analyzer settings together in an issue-backed change.
