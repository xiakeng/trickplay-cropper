# Code Maps

Compact navigation for contributors and coding agents: each map names repository
targets, key symbols, responsibilities, relationships, and test entry points, so a
reader can choose a route before opening implementation files. Maps never restate
implementation details or business specifications — those live in the code and in the
[business documentation](../business/README.md).

| Map | Answers |
|---|---|
| [Request paths](request-paths.md) | How a GET or Trickplay Frame Probe request travels from route to response |
| [Caching](caching.md) | Where Preview Cache Coordination, both observation caches, and cleanup live |
| [Tests](tests.md) | Which suite and entry point proves which behavior |
| [Tooling](tooling.md) | Where the Integration Harness and the build, release, and analysis tooling live |

## Maintenance

- A structural change updates every Code Map it affects in the same pull request.
- Semantic and symbol accuracy are review responsibilities: a map naming a target or
  symbol that no longer exists is a defect of the change that broke it.
- The [root README](../../README.md) stays an implementation-phase, same-pull-request
  responsibility of the change that alters documented behavior.

## Size limit

Every Markdown source in this collection — this index and any future nested map — is
limited to 1,000 `cl100k_base` tokens over its complete source. The limit applies
nowhere else in the repository.
