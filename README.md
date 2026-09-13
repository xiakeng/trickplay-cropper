# Trickplay Cropper

Trickplay Cropper is a Jellyfin server plugin that exposes authenticated
client-side Frame Timelines and single-frame Trickplay Previews from
Jellyfin-owned Source Sprites.

The plugin serves one calculation timeline for an authorized playback source and
one JPEG frame for an authorized playback position, cropped from trickplay data
Jellyfin already generated. It never generates, modifies, or repairs that data.
What it adds on top:

- **Client-cached Frame Timelines.** `GET /TrickplayCropper/Videos/{itemId}/FrameTimeline`
  returns exactly `intervalTicks` and `frameCount` after the same current-user,
  logical-item, Media Source membership, and Source Video visibility checks as a
  Preview. The response is private and no-cache; clients use it for local Frame
  Index selection, while every later Preview request repeats authorization.

- **Adaptive resolution selection.** Every request derives the Selected Trickplay
  Resolution from the server's current Trickplay Resolution Targets — minimum
  target, Jellyfin's own normalization rule for the Media Source, and an exact
  generated-metadata match. No fallback width, no nearest substitute.
- **The Trickplay Frame Probe.** A bodyless HTTP HEAD operation answers *which
  frame does this position select?* for an Item and real Media Source accepted by
  Jellyfin's ordinary endpoint policy, then stops before user-scoped preview
  authorization or any image work. It remains available until the next-stage
  direct Frame Index slice retires it.
- **Bounded source and metadata reuse.** A warm HEAD reuses immutable Item/source
  membership, matched-source width, and generated metadata for 30 minutes; explicit
  absence lives for 5 minutes. GET always rechecks current user authority and source
  facts, publishes only verified user-neutral positives, and refreshes every
  non-negative-short-circuited metadata observation — including JPEG HITs and
  conditional requests.
- **User-scoped authorization with concealment.** Frames reach only callers who
  may play the logical video; GET makes hidden Items answer exactly like absent
  ones, and does not treat a server API key as a user.
- **A per-entry cache.** One Preview Cache Entry per derived artifact under the
  plugin-owned Cache Tree, coordinated by tree leases and entry locks, kept honest
  by a source version stamp, and emptied by a Jellyfin scheduled task.
- **Stable structured Debug events.** A fixed EventId/EventName protocol exposes
  cache disposition, lock, lease, and permit waits plus Frame Index and sprite
  index — redaction-safe, Debug-only, and behavior-neutral.
- **Human-gated automated releases.** Every push to `main` refreshes one pending
  Release Pull Request; merging it publishes the installable ZIP as a stable
  GitHub Release and updates the Jellyfin repository manifest.

The complete business documentation — participants, lifecycle, and design, with a
reading path and a route-by-question table — lives under
[docs/business](docs/business/README.md).

## Compatibility

- Jellyfin Server: `10.11.11`
- Plugin ABI: `10.11.0.0`
- Target framework: `net9.0`
- .NET SDK: `9.0.317`
- Plugin version: `1.0.0.0`

## Install, update, and roll back

Every stable version is published as a GitHub Release tagged `v<version>` and
titled `Trickplay Cropper <version>`, carrying the validated JPRM ZIP as its only
asset. The ZIP is flat and contains exactly:

```text
Jellyfin.Plugin.TrickplayCropper.dll
meta.json
```

**Install manually.** Download the ZIP from the release page, extract those two
files into one dedicated direct child directory of Jellyfin's plugins directory,
then restart Jellyfin.

**Install, update, and roll back through the catalog.** The repository root keeps a
Jellyfin repository manifest (`manifest.json`) with one entry for every published
stable release — versioned source URL, MD5 checksum, and timestamp derived from the
actual published ZIP — in descending version order. Add this repository to Jellyfin's
plugin repositories, and Jellyfin offers the plugin for installation, offers
compatible updates as new stable versions appear, and supports exact-version
rollback by letting you select any version the manifest retains. The manifest
contains published stable releases only; drafts, prereleases, failed builds, and
missing assets are never catalogued.

Rolling back manually is the same operation as installing: download the ZIP of the
exact version you want from its GitHub Release and replace the plugin directory's
contents, then restart Jellyfin.

## Build and test

Local builds require the .NET SDK version pinned in `global.json` (currently
9.0.317) and Python 3 with `tiktoken` 0.12.0. ComponentTests launch the
`python3` resolved from `PATH`, so that interpreter must provide `tiktoken`.
On Ubuntu 26.04, install the Python prerequisite and verify both tool versions with:

```bash
sudo apt install python3-tiktoken
dotnet --version
python3 -c 'from importlib.metadata import version; print(version("tiktoken"))'
```

The committed NuGet lock files are enforced for every restore.
CI also directly enforces repository file-size boundaries, compact Code Maps, and
the existence of every local target linked by a Code Map.

```bash
dotnet restore TrickplayCropper.sln --locked-mode
dotnet build TrickplayCropper.sln --configuration Release --no-restore
dotnet test tests/Jellyfin.Plugin.TrickplayCropper.UnitTests --configuration Release --no-build --no-restore
dotnet test tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests --configuration Release --no-build --no-restore
```

The component tests are Linux-specific because they privately provide the
native assets that Jellyfin supplies at runtime. Production references
Jellyfin and SkiaSharp only at compile time.

Tests tied to Jellyfin server behavior belong to ComponentTests, including
HTTP contracts, server log formats, deployment/recovery, operator input, and
plugin host activation, even when test doubles avoid a live connection.
ComponentTests also runs the harness's Python filesystem, SQLite WAL, and
Landlock checks against temporary fixtures. These checks require Python 3 and
Linux Landlock ABI 3 or later; they never use the operator's `harness.json`,
invoke sudo, or restart a service. UnitTests has no Integration Harness project
reference. The repository-structure contract cases additionally require
`tiktoken` 0.12.0. CI runs both test projects; actual host deployment remains a
separate manual Integration Harness invocation.

## Package

CI runs the commit-pinned JPRM action against only the production project. A
matching local JPRM checkout can build the same package:

```bash
jprm plugin build src/Jellyfin.Plugin.TrickplayCropper \
  --output artifacts/package \
  --dotnet-framework net9.0
dotnet run --project tools/TrickplayCropper.PackageValidator \
  --configuration Release --no-build --no-restore -- \
  artifacts/package/trickplay-cropper_1.0.0.0.zip \
  src/Jellyfin.Plugin.TrickplayCropper/build.yaml
sha256sum artifacts/package/trickplay-cropper_1.0.0.0.zip
```

Generated package outputs are never committed; the installable ZIP lives on the
GitHub Release, and CI's Package Validator is the package-install contract.

## Manual local Integration Harness

The Integration Harness is a manually invoked, no-mock `net9.0` console program
that deploys a Debug build of the plugin to the local Jellyfin host, runs four
fixed smoke cases (invalid token, GET concealment, playback boundaries, Scrub
Storm), and restores the host logging configuration afterwards. It targets the
local native Jellyfin installation at `http://localhost:8096` with fixed
`/etc/jellyfin` and `/var/lib/jellyfin` paths, and requires Python 3 plus an
unprivileged account with interactive sudo. Each run crosses exactly two `sudo`
boundaries and performs exactly two Jellyfin restarts.

Copy `harness.example.json` to the gitignored root `harness.json` and fill in an
administrator **user access token** (not a server API key), exactly two playable
video Item IDs, and one Item that exists but is invisible to that user. Keep the
file private (`chmod 600 harness.json`); it grants the user's administrator
access and is only ever sent to localhost in an HTTP authorization header.

The concealed-Item case is a GET authorization assertion only. A successful HEAD
is calculation availability, not permission evidence. Successful GET responses,
including `304 Not Modified`, expose the selected Frame Index in
`X-Trickplay-Frame-Index`; the ETag remains the independent representation identity.
The current live harness does not expose backing source/metadata read counters, fake-time
expiry, or publication barriers, so source-cache I/O and race coverage remains in the
HTTP component seam with host API test doubles rather than the live fixture. Default,
local alternate, linked, and eligible dynamic source compatibility is grounded in the
pinned Jellyfin source; the tests do not run its real provider or linked-source graph.

```sh
# Wait for API readiness and validate supplied subjects; no elevation or restart.
dotnet run --project tools/TrickplayCropper.IntegrationHarness -- --check
# Deploy, run all four smoke cases including Scrub Storm, and restore.
dotnet run --project tools/TrickplayCropper.IntegrationHarness
# Exercise a deliberate assertion failure after the real smoke cases.
dotnet run --project tools/TrickplayCropper.IntegrationHarness -- --verify-restoration
```

Output goes to two places: stdout carries only ordinal subject labels and
non-secret numeric results (never the token or Item IDs), and every full run
writes an English Markdown Scrub Storm report to the gitignored
`test-output/scrub-storm-*.md`, keeping previous reports (`--check` writes none).
Exit code zero means all four cases plus restoration and final health passed;
`--verify-restoration` deliberately exits nonzero even when recovery succeeds.
After the run the Debug plugin and the populated Cache Tree remain in place —
only logging configuration is restored.
