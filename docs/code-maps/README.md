# Code Maps

Documents are based on commit 'c66fd8cc0699944e2f403a399bf1e42c68de4e40'

Compact navigation for contributors and coding agents: each map names repository
targets, key symbols, responsibilities, relationships, and test entry points, so a
reader can choose a route before opening implementation files. Maps never restate
implementation details or product contracts. Their paths and relationships are derived
directly from the current code, tests, workflows, and repository structure.

| Map | Answers |
|---|---|
| [Request paths](request-paths.md) | How Preview GET and Frame Timeline requests travel from route to response |
| [Caching](caching.md) | Where the disk Preview Cache, coordination, and cleanup live |
| [Tests](tests.md) | Which suite and entry point proves which behavior |
| [Tooling](tooling.md) | Where the Integration Harness and the build, release, and analysis tooling live |
