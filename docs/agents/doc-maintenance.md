# Documentation maintenance

## Isolation

Keep `docs/code-maps/` and `docs/business/` completely independent. Neither
surface may link to, restate, or use the other as a source. When maintaining
both, run two separate reviews from their respective README base commits.

Derive Code Map claims directly from the current code, tests, workflows, and
repository structure. Derive business documentation directly from the current
product behavior, contracts, tests, and domain sources.

## Incremental review

For each surface being maintained:

1. Read the surface root README (`docs/code-maps/README.md` or
   `docs/business/README.md`) and resolve the full SHA in `Documents are based
   on commit '<sha>'`.
2. Fetch `origin/main` and record its current full SHA as the review target.
   Verify that the base commit is an ancestor of the target; stop without
   changing the base if it is not.
3. Enumerate every first-parent commit in `<base>..origin/main`, oldest first.
   For each commit, identify its associated PR and read all linked tickets and
   their parent spec tickets, including relevant discussion, to understand the
   intent, acceptance criteria, and scope. If no PR or parent spec exists,
   record that absence; unresolved or inaccessible references leave the review
   incomplete. Then inspect the code, test, workflow, and repository-structure
   changes it introduced in that context and account for their effect on every
   relevant document in this surface. Verify intended behavior against the
   target implementation before documenting it as delivered.
4. Update stale documents so they describe the final target commit. Keep each
   claim within this surface's scope and verify it against the target's source,
   not against the other documentation surface.
5. Verify that the completed surface contains no link, restatement, or source
   dependency on the other documentation surface.
6. After every commit is accounted for and all required documentation changes
   are complete, replace the README base SHA with the recorded target SHA. Do
   this even when no other document needed a change.

Never advance the base commit when the commit review or required documentation
updates are incomplete.
