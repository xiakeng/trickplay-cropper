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
