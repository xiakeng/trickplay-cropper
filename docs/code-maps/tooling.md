# Tooling map

Three workflows gate and ship the repository, and a fourth keeps business-documentation
analysis pending; four console tools support them and the manual Integration Harness.
The build manifest `src/Jellyfin.Plugin.TrickplayCropper/build.yaml` is the single
version source for release automation. Test locations are on the [tests map](tests.md).

## Workflows

| Target | Responsibility |
|---|---|
| [verify_repository_structure.py](../../.github/scripts/verify_repository_structure.py) | CI-only implementation of tracked source/script line limits and Code Map token/link checks |
| [ci.yml](../../.github/workflows/ci.yml) | Every push and pull request: repository size/link contracts, restore, format, build, both test suites, JPRM package, validation, checksum |
| [auto-release.yml](../../.github/workflows/auto-release.yml) | Every push to `main`: plan the next version and open or update the pending release pull request |
| [publish-release.yml](../../.github/workflows/publish-release.yml) | On release-PR merge: re-run the gates, publish the stable GitHub Release, submit the manifest pull request |
| [business-docs-analysis.yml](../../.github/workflows/business-docs-analysis.yml) | On every pull-request merge to `main`: keep at most one open `docs:business-analysis` issue |

## Integration Harness

[tools/TrickplayCropper.IntegrationHarness](../../tools/TrickplayCropper.IntegrationHarness) —
manually invoked, no-mock verification against the local Jellyfin host.

| Target | Key symbols | Responsibility |
|---|---|---|
| [HarnessApplication.cs](../../tools/TrickplayCropper.IntegrationHarness/HarnessApplication.cs) | `RunAsync` | Modes (`--check`, `--verify-restoration`), input parsing, run sequencing |
| [SmokeCases.cs](../../tools/TrickplayCropper.IntegrationHarness/SmokeCases.cs) | `RunAsync` | Invalid token, concealed GET, playback boundaries |
| [ScrubStorm.cs](../../tools/TrickplayCropper.IntegrationHarness/ScrubStorm.cs) | `RunAsync`, `VerifyQuiescenceAsync` | Coordinated HEAD/GET storm |
| [ScrubStormReport.cs](../../tools/TrickplayCropper.IntegrationHarness/ScrubStormReport.cs) | `WriteAsync`, `ToMarkdown` | Client-observed statistics and the gitignored Markdown report |
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
`ManifestBuilderSpecs.cs`); workflow definitions by `ReleaseWorkflowContractSpecs.cs`,
`PublicationWorkflowContractSpecs.cs`, and `ManifestWorkflowContractSpecs.cs`; harness
machinery by the ComponentTests listed on the [tests map](tests.md).
