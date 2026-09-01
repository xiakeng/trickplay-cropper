# Contribution Workflow

1. Every change must have a GitHub issue.
2. A single coordinating session must bootstrap implementation worktrees before
   dispatching `/implement`. For each issue, it serially fetches `origin/main`
   immediately before creating the issue branch and posts the fetched commit to
   the issue as its implementation base.
3. The coordinating session creates `issue-<number>` at that exact commit in a
   dedicated worktree, then assigns exactly one implementation session to it.
   Implementation sessions work only in their assigned worktrees and do not
   create, remove, or reassign worktrees or branches.
4. After the coordinator completes bootstrap, it may dispatch implementation
   sessions concurrently only when their issues have no open blocking
   relationship. Keep each session's uncommitted state in its owning worktree;
   do not use `git stash` during concurrent implementation.
5. Open a pull request to this repository's `main` branch unless explicitly told otherwise.
6. End the pull request description with:

   `Closes #<issue-number>`
7. After opening the pull request, run `/code-review` against its exact base and
   current head, and publish every actionable finding on the pull request.
8. Resolve every actionable finding and push the fixes to the pull request branch.
9. Repeat steps 7 and 8 until `/code-review` reports no actionable findings for
   the pull request's current head.
10. Leave the pull request open after review. Merge it only when the user explicitly
   requests the merge.

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

For C# code reviews, follow `docs/agents/csharp-code-review.md`.
