# Contribution Workflow

1. Every change must have a GitHub issue.
2. Create an issue branch named `issue-<number>` and make the change there.
3. Open a pull request to this repository's `main` branch unless explicitly told otherwise.
4. End the pull request description with:

   `Closes #<issue-number>`

## Agent references

- **Issue tracking**: Before reading or changing GitHub issues, follow
  `docs/agents/issue-tracker.md`.
- **Triage**: Before applying issue states or labels, follow
  `docs/agents/triage-labels.md`.
- **Domain work**: Before exploring or changing product behavior, follow
  `docs/agents/domain.md` and use the vocabulary in `CONTEXT.md`.
- **C# changes**: Before creating, modifying, or refactoring C# code, follow
  `docs/agents/csharp-coding-standard.md`.
- **C# review**: Before reviewing C# changes, follow
  `docs/agents/csharp-code-review.md`.
