# Contribution Workflow

1. Every change must have a GitHub issue.
2. Immediately before creating the issue branch, fetch `origin/main`.
3. Create `issue-<number>` from the fetched `origin/main` commit and make the
   change there.
4. Open a pull request to this repository's `main` branch unless explicitly told otherwise.
5. Leave the pull request open after previous steps finished. Merge it only when the user explicitly
   requests the merge.

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the five default canonical triage labels. See `docs/agents/triage-labels.md`.

### Domain docs

This repository uses a single-context domain docs layout. See `docs/agents/domain.md`.

### Code Maps

Start codebase navigation from `docs/code-maps/README.md`. Use the semantic Code Maps to identify the relevant modules, entry points, and execution paths before performing broad repository searches.

Treat Code Maps as navigation aids, not authoritative source code: verify conclusions against the current implementation. If a map is incomplete or stale, continue by searching the codebase; do not update `docs/code-maps/` unless the current issue is labeled `doc-maintain`.

### C# coding standard

For C# code changes, apply `docs/agents/csharp-guidelines.md`.

### C# code review

For C# code reviews, follow `docs/agents/csharp-code-review.md`.
