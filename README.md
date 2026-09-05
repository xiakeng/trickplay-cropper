# Trickplay Cropper

Trickplay Cropper is a Jellyfin server plugin that exposes authenticated,
single-frame Trickplay Previews from Jellyfin-owned Source Sprites.

This repository contains the complete Trickplay Cropper v1 implementation,
its deterministic unit and component verification, and its reproducible
packaging pipeline.

## Compatibility

- Jellyfin Server: `10.11.11`
- Plugin ABI: `10.11.0.0`
- Target framework: `net9.0`
- .NET SDK: `9.0.317`
- Plugin version: `1.0.0.0`

## Build and test

The committed NuGet lock files are enforced for every restore.

```bash
dotnet restore TrickplayCropper.sln --locked-mode
dotnet build TrickplayCropper.sln --configuration Release --no-restore
dotnet test tests/Jellyfin.Plugin.TrickplayCropper.UnitTests --configuration Release --no-build --no-restore
dotnet test tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests --configuration Release --no-build --no-restore
```

The component tests are Linux-specific because they privately provide the
native assets that Jellyfin supplies at runtime. Production references
Jellyfin and SkiaSharp only at compile time.

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

The validated ZIP is flat and contains exactly:

```text
Jellyfin.Plugin.TrickplayCropper.dll
meta.json
```

## Release evidence

A successful CI workflow, its source commit, the validated ZIP, and the
matching SHA-256 file are the complete required v1 release evidence. CI does
not claim proof of a live Jellyfin plugin load, host-provided Skia resolution,
live authentication or manager integration, Dashboard persistence, or decoding
of a real Jellyfin Source Sprite.

For manual installation, extract those two files into one dedicated direct
child directory of Jellyfin's plugins directory, then restart Jellyfin.

## Manual local Integration Harness

The deployment milestone is a manually invoked `net9.0` console program. It
validates a human-supplied administrator **user access token** (not a server API
key), exactly two playable video Item IDs, and one existing Item concealed from
that user. Copy `harness.example.json` to the gitignored root `harness.json` and
fill it in. Keep this file private (`chmod 600 harness.json`); it grants the
user's administrator access and is sent only in an HTTP authorization header to
`http://localhost:8096`. The harness never prints its contents or authenticates
with a password.

Use the local native Jellyfin installation, Python 3 (standard library only),
.NET SDK from `global.json`, and an unprivileged account with interactive sudo
access. The harness uses fixed `/etc/jellyfin` and `/var/lib/jellyfin` paths. Its
read-only, exact-ID SQLite query proves the invisible Item exists independently
of the user-scoped HTTP 404; the account must be able to read Jellyfin's database
and existing WAL/shared memory. It does not enumerate or provision media.

```sh
dotnet restore TrickplayCropper.sln --locked-mode
# Validate the supplied subjects only; no elevation, deployment, or restart.
dotnet run --project tools/TrickplayCropper.IntegrationHarness -- --check
# Perform one deployment and restoration cycle.
dotnet run --project tools/TrickplayCropper.IntegrationHarness
# Exercise a deliberate assertion failure after the real deployment gates.
dotnet run --project tools/TrickplayCropper.IntegrationHarness -- --verify-restoration
```

Each cycle builds the Debug plugin, then crosses exactly two `sudo` boundaries.
Sudo may reuse its normal timestamp; there is no unattended elevation or extra
confirmation prompt. Privileged Phase 1 atomically creates the single sibling
`logging.json.bak` snapshot, preserving logging bytes and metadata. Acquiring the
snapshot before destructive work prevents concurrent runs from both passing a
check and then overwriting each other's recovery data. An existing snapshot
blocks all mutation and requires human inspection before removal.

Phase 1 deletes only installations whose `meta.json` GUID matches this plugin,
empties only its `preview-v1` Cache Tree, deploys only the Debug DLL and PDB as
`jellyfin:jellyfin` (`0755` directory, `0644` files), adds only the plugin's Debug
logging override, and restarts Jellyfin. It leaves logging defaults and sinks
intact. The driver requires host health, the built version's Active status, a
real JPEG GET, and a fresh structured plugin Debug event from the newest Jellyfin
log. Existing event IDs and fields also travel in a JSON message envelope so
ordinary text sinks preserve them without changing their configuration.

Privileged Phase 2 runs after successful, failed, or cancelled verification: it
restores logging byte-for-byte and preserves metadata, removes the snapshot,
restarts Jellyfin, and independently waits for health. A started cycle has a
two-restart budget; two separate normal/failure demonstrations therefore use
four restarts. Exit zero requires verification, restoration, and final health.
`--verify-restoration` deliberately exits nonzero, even when recovery succeeds.
The retained state is the Debug plugin and the populated Cache Tree. No evidence,
transcript, run-lock, or other state files are written. A partial snapshot,
SIGKILL, power loss, lost restoration privilege, or failed restart requires human
recovery; inspect any surviving snapshot before starting again.

This milestone covers deployment and recovery only. The four fixed smoke cases
(invalid token, invisible Item, playback boundaries, and Scrub Storm) belong to
#76 and #77. Other live gaps include playback-policy 403, alternate Media Sources,
every 500 shape, source-width clamping, multiple targets, media-side Source
Sprites, absent metadata/thumbnails/sprites, cleanup, Cache Tree seeding and lease
contention. Debug DLL deployment does not verify the shippable ZIP; CI's Package
Validator owns that check. Default CI compiles the harness and runs its isolated
unit/filesystem checks, but never executes the live harness or uses sudo.
