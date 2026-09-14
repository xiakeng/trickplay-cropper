# Tooling map

Three workflows gate and ship the repository; three release tools support them alongside
the manual Integration Harness.
The build manifest `src/Jellyfin.Plugin.TrickplayCropper/build.yaml` is the single
version source for release automation. Test locations are on the [tests map](tests.md).

## Workflows

| Target | Responsibility |
|---|---|
| [verify_repository_structure.py](../../.github/scripts/verify_repository_structure.py) | CI-only implementation of tracked source/script line limits and Code Map token/link checks |
| [ci.yml](../../.github/workflows/ci.yml) | Every push and pull request: repository size/link contracts, restore, format, build, both test suites, JPRM package, validation, checksum |
| [auto-release.yml](../../.github/workflows/auto-release.yml) | Qualifying pushes to `main`: plan the next version and open or update the pending release pull request |
| [publish-release.yml](../../.github/workflows/publish-release.yml) | On release-PR merge: re-run the gates, publish the stable GitHub Release, submit the manifest pull request |

## Integration Harness

[tools/TrickplayCropper.IntegrationHarness](../../tools/TrickplayCropper.IntegrationHarness) —
manually invoked, no-mock verification against the local Jellyfin host.

| Target | Key symbols | Responsibility |
|---|---|---|
| [HarnessApplication.cs](../../tools/TrickplayCropper.IntegrationHarness/HarnessApplication.cs) | `RunAsync` | Modes (`--check`, `--verify-restoration`), input parsing, run sequencing |
| [SmokeCases.cs](../../tools/TrickplayCropper.IntegrationHarness/SmokeCases.cs) | `RunAsync` | Invalid token, concealed Timeline/Preview, timeline oracle, and playback boundaries |
| [ScrubStorm.cs](../../tools/TrickplayCropper.IntegrationHarness/ScrubStorm.cs) | `RunAsync`, `VerifyQuiescenceAsync` | Coordinated direct Frame Index GET storm |
| [ScrubStormReport.cs](../../tools/TrickplayCropper.IntegrationHarness/ScrubStormReport.cs) | `SendAsync`, `ToMarkdown` | Client-observed GET/cache, JPEG, Debug-event, cleanup, and restoration evidence |
| [DeploymentCycle.cs](../../tools/TrickplayCropper.IntegrationHarness/DeploymentCycle.cs) | `RunAsync` | Prepare, verify, and restore around each verification run |
| [host_operation.py](../../tools/TrickplayCropper.IntegrationHarness/host_operation.py) | prepare, restore | Privileged deployment and logging restoration |
| [LocalJellyfin.cs](../../tools/TrickplayCropper.IntegrationHarness/LocalJellyfin.cs) | `ValidateAsync`, `WaitForHealthAsync` | Read-only host gates and deployment verification |

## Build and release tools

| Target | Responsibility |
|---|---|
| [tools/TrickplayCropper.PackageValidator](../../tools/TrickplayCropper.PackageValidator) | Validates the JPRM ZIP against the install contract; runs in CI and publication |
| [tools/TrickplayCropper.ReleasePlanner](../../tools/TrickplayCropper.ReleasePlanner) | Computes the next version and changelog for the pending release pull request |
| [tools/TrickplayCropper.ManifestBuilder](../../tools/TrickplayCropper.ManifestBuilder) | Builds the Jellyfin repository manifest entry from a published release |

Tool behavior is pinned by UnitTests (`PackageValidatorSpecs.cs`, `ReleasePlannerSpecs.cs`,
`ManifestBuilderSpecs.cs`); harness machinery by the ComponentTests listed on the
[tests map](tests.md).
