# Contribution Workflow

1. Every change must have a GitHub issue.
2. Create an issue branch named `issue-<number>` and make the change there.
3. Open a pull request to this repository's `main` branch unless explicitly told otherwise.
4. End the pull request description with:

   `Closes #<issue-number>`

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the five default canonical triage labels. See `docs/agents/triage-labels.md`.

### Domain docs

This repository uses a single-context domain docs layout. See `docs/agents/domain.md`.

### C# coding standard

For C# code changes, apply `docs/agents/csharp-guidelines/SKILL.md` and
`docs/agents/csharp-guidelines-overrides.md`.

### C# code review

For C# code reviews, apply `docs/agents/csharp-guidelines/SKILL.md`,
`docs/agents/csharp-code-review.md`, and
`docs/agents/csharp-guidelines-overrides.md`.
