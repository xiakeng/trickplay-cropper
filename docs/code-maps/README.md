# Code Maps

Documents are based on commit 'a0c24f109cb3bed28d970e6cad03f4c5c0a1f7ae'

Compact navigation for contributors and coding agents: each map names repository
targets, key symbols, responsibilities, relationships, and test entry points, so a
reader can choose a route before opening implementation files. Maps never restate
implementation details or product contracts. Their paths and relationships are derived
directly from the current code, tests, workflows, and repository structure.

| Map | Answers |
|---|---|
| [Request paths](request-paths.md) | How Frame Timeline and Preview GET requests travel from route to response |
| [Caching](caching.md) | Where Preview Cache Coordination, disk entries, and cleanup live |
| [Tests](tests.md) | Which suite and entry point proves which behavior |
| [Tooling](tooling.md) | Where the Integration Harness and the build and release tooling live |
