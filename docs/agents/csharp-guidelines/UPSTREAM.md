# Upstream Provenance

The vendored `SKILL.md`, `references/*.md`, and `LICENSE.md` files come from the
official CSharpGuidelines repository.

- Upstream: <https://github.com/dennisdoomen/CSharpGuidelines>
- Creator: Dennis Doomen and CSharpGuidelines contributors
- Release: `6.0.0`
- Commit: `7ee0a68bf9458053fa2093ce6726bcbd75c8c560`
- Source directory: `Skills/csharp-guidelines/`
- Retrieved: 2026-09-01
- License: Creative Commons Attribution-ShareAlike 4.0; see `LICENSE.md`

The upstream files are stored unmodified. `UPSTREAM.md` is local provenance
metadata and is not part of the upstream Skill.

## Update procedure

1. Select a released upstream tag and resolve its full commit SHA.
2. Replace only `SKILL.md`, `references/`, and `LICENSE.md` from that commit.
3. Verify those files byte-for-byte against upstream.
4. Review release notes and reconcile the adoption profile, repository
   overrides, `.editorconfig`, analyzers, and CI.
5. Update the release, commit, and retrieval date above in the same change.
