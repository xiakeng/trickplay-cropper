# Jellyfin Custom Repository and GitHub Release Contract

Research date: 2026-09-02. This note answers GitHub issue #46 for Jellyfin
Server 10.11.x and the repository's pinned JPRM v1.1.1 action.

## Decision

The root `manifest.json` must be a Jellyfin repository manifest: a JSON array
containing one plugin object. It is not the JPRM `build.yaml` schema. Keep the
plugin GUID stable because Jellyfin uses it to associate updates, and publish
this shape:

```json
[
  {
    "guid": "630fb758-9a29-4f2c-a54c-95793651bb8a",
    "name": "Trickplay Cropper",
    "description": "...",
    "overview": "...",
    "owner": "xiakeng",
    "category": "General",
    "versions": [
      {
        "version": "1.0.1.0",
        "changelog": "...",
        "targetAbi": "10.11.0.0",
        "sourceUrl": "https://github.com/xiakeng/trickplay-cropper/releases/download/v1.0.1.0/trickplay-cropper_1.0.1.0.zip",
        "checksum": "32-lowercase-hexadecimal-MD5",
        "timestamp": "2026-09-02T07:00:00Z"
      }
    ]
  }
]
```

The top-level fields and optional `imageUrl` come directly from Jellyfin's
[`PackageInfo`](https://github.com/jellyfin/jellyfin/blob/b3766b00d4c5ae38589774b30f4f1e0579a9619f/MediaBrowser.Model/Updates/PackageInfo.cs#L25-L81);
the six version fields come from
[`VersionInfo`](https://github.com/jellyfin/jellyfin/blob/b3766b00d4c5ae38589774b30f4f1e0579a9619f/MediaBrowser.Model/Updates/VersionInfo.cs#L13-L63).
The current [official Jellyfin manifest](https://repo.jellyfin.org/files/plugin/manifest.json)
uses the same array and version-entry shape.

## Field and history semantics

- `version` is a .NET `System.Version`, not SemVer text. Publish all four
  numeric components. Sort `versions` by that numeric value, newest first.
  Jellyfin independently chooses compatible entries in descending numeric
  order, and an exact version can be requested for rollback
  ([selection](https://github.com/jellyfin/jellyfin/blob/b3766b00d4c5ae38589774b30f4f1e0579a9619f/Emby.Server.Implementations/Updates/InstallationManager.cs#L252-L293),
  [install API](https://github.com/jellyfin/jellyfin/blob/b3766b00d4c5ae38589774b30f4f1e0579a9619f/Jellyfin.Api/Controllers/PackageController.cs#L76-L121)).
  For this project an automatic patch bump is `1.0.0.0` to `1.0.1.0`, with
  major/minor changes supplied explicitly.
- `targetAbi` is the minimum compatible Jellyfin server version. Jellyfin
  removes entries whose `targetAbi` is greater than the running server; there
  is no maximum-ABI field in the current model
  ([filter](https://github.com/jellyfin/jellyfin/blob/b3766b00d4c5ae38589774b30f4f1e0579a9619f/Emby.Server.Implementations/Updates/InstallationManager.cs#L187-L209)).
  Preserve older releases with their original ABI so older servers can still
  select them.
- `sourceUrl` must be the immutable, public HTTPS URL of the exact ZIP asset.
  A versioned GitHub Release URL is suitable; never use `latest/download`, an
  Actions artifact URL, or a mutable branch URL.
- `checksum` is the MD5 of the exact ZIP bytes. Jellyfin downloads the ZIP and
  compares MD5 case-insensitively before extraction
  ([installer](https://github.com/jellyfin/jellyfin/blob/b3766b00d4c5ae38589774b30f4f1e0579a9619f/Emby.Server.Implementations/Updates/InstallationManager.cs#L549-L568)).
  The existing SHA-256 sidecar is useful independently but cannot populate
  this field.
- `timestamp` is the package build time in UTC RFC 3339 form with `Z`. Jellyfin
  parses it into the installed plugin manifest
  ([population](https://github.com/jellyfin/jellyfin/blob/b3766b00d4c5ae38589774b30f4f1e0579a9619f/Emby.Server.Implementations/Plugins/PluginManager.cs#L424-L438));
  it does not determine update ordering. Preserve the first published value.
- Keep every non-withdrawn, published, non-prerelease version in the history.
  This enables compatible update selection and exact-version rollback. Draft,
  prerelease, failed, or missing-asset versions must never enter the manifest.
  To withdraw a release, remove only its version entry so new installs and
  catalog rollbacks stop discovering it; leave other history intact. Delete a
  Release asset only for an explicit security revocation because existing
  manifest URLs would break.

JPRM v1.1.1 confirms these choices: it normalizes versions to four components,
packages the configured artifacts plus `meta.json`, emits a `.md5sum`, and
copies the metadata build timestamp into a repository entry
([packaging](https://github.com/oddstr13/jellyfin-plugin-repository-manager/blob/9497a0a499416cc572ed2e07a391d9f943a37b4d/jprm/__init__.py#L396-L519),
[manifest generation](https://github.com/oddstr13/jellyfin-plugin-repository-manager/blob/9497a0a499416cc572ed2e07a391d9f943a37b4d/jprm/__init__.py#L522-L596)).
Its history merge replaces the same version, retains the others, and sorts
descending
([merge](https://github.com/oddstr13/jellyfin-plugin-repository-manager/blob/9497a0a499416cc572ed2e07a391d9f943a37b4d/jprm/__init__.py#L599-L623)).

## Retry-safe publisher constraints

Treat one qualifying `main` push as one release transaction, after its CI
build and tests pass. The publisher must satisfy all of the following:

1. Queue release jobs under one fixed concurrency group with `queue: max` and
   no cancellation. GitHub otherwise retains only one pending run; `queue:
   max` serializes up to 100 waiting runs
   ([Actions concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency#example-queueing-multiple-pending-runs)).
   A single push produces at most one release, regardless of its commit count.
2. Pin the source commit SHA and compute the next patch only after entering the
   release queue. Use one version value for the tag (`v<version>`), release
   title, `build.yaml`, project/assembly/file version, JPRM version override,
   ZIP filename, ZIP `meta.json`, and root manifest entry. Fail before
   publication if any value differs.
3. Use the pinned JPRM action's `artifact` output as the sole ZIP
   ([action contract](https://github.com/oddstr13/jellyfin-plugin-repository-manager/blob/9497a0a499416cc572ed2e07a391d9f943a37b4d/action.yaml#L9-L42)).
   Validate the ZIP contents and identities, calculate both MD5 for Jellyfin
   and SHA-256 for diagnostics, and derive the manifest timestamp from that
   ZIP's `meta.json`; do not regenerate it later.
4. Create or reconcile a draft Release for the exact tag and commit, upload
   the ZIP, verify its name, size, and digest, then publish. GitHub recommends
   draft, attach, publish for immutable releases
   ([immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases#best-practices-for-publishing-immutable-releases)).
   Enable immutable releases if available so the published tag and assets
   cannot move.
5. Make retries reconcile, not overwrite: find the Release by tag; verify that
   the tag targets the expected release commit; reuse an already uploaded
   asset only when its bytes match; remove only an incomplete `starter` asset;
   and fail closed on any other mismatch. GitHub rejects duplicate asset names
   and documents that a failed upload may leave a `starter` asset
   ([asset API](https://docs.github.com/en/rest/releases/assets#upload-a-release-asset)).
   This is necessary because JPRM embeds a fresh build timestamp, so rebuilding
   after an upload does not promise identical ZIP bytes.
6. Publish before committing the new manifest entry, then update the latest
   `main` with an additive, descending history merge. This allows a retry to
   repair a missing manifest commit without exposing a manifest URL to a draft
   asset. A completed retry is a no-op after re-verifying every invariant.
7. Give only the publishing job `contents: write`; other jobs remain
   read-only. `contents: write` authorizes release creation
   ([token permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#permissions)).
   Push the generated manifest/version maintenance commit with
   `GITHUB_TOKEN`: GitHub does not start another push workflow from that token,
   preventing a release loop
   ([token recursion rule](https://docs.github.com/en/actions/concepts/security/github_token#when-github_token-triggers-workflow-runs)).
   Path classification should still exclude the generated files as defense in
   depth.

The final gate must download the public `sourceUrl` and prove: tag and Release
version agree; the asset MD5 equals `checksum`; its embedded `meta.json`, DLL,
and committed version files agree; `targetAbi` is the approved Jellyfin ABI;
the manifest contains exactly one entry for the version; and all older
non-withdrawn entries remain unchanged.
