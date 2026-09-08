# Code Maps

Documents are based on commit '7d7402e5f1bce03b80aeb1f4587cf6e676c92176'

Compact navigation for contributors and coding agents: each map names repository
targets, key symbols, responsibilities, relationships, and test entry points, so a
reader can choose a route before opening implementation files. Maps never restate
implementation details or product contracts. Their paths and relationships are derived
directly from the current code, tests, workflows, and repository structure.

| Map | Answers |
|---|---|
| [Request paths](request-paths.md) | How a GET or Trickplay Frame Probe request travels from route to response |
| [Caching](caching.md) | Where Preview Cache Coordination, both observation caches, and cleanup live |
| [Tests](tests.md) | Which suite and entry point proves which behavior |
| [Tooling](tooling.md) | Where the Integration Harness and the build, release, and analysis tooling live |
