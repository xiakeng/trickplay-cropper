# Tests map

[UnitTests](../../tests/Jellyfin.Plugin.TrickplayCropper.UnitTests) proves pure contracts
with no host dependency: math, outcome mapping, release tools, and repository-file
contracts. [ComponentTests](../../tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests)
proves Jellyfin-server behavior — HTTP contracts, disk and encoder behavior, host
activation, and the harness's own machinery — with test doubles instead of a live host.
Both suites run in CI; the live host is the manually invoked harness
([tooling map](tooling.md)).

Each file below lives in the named suite's directory.

| To prove or change… | Entry point |
|---|---|
| GET/HEAD HTTP behavior: authorization, concealment, conditional requests, warm probe reuse, real Kestrel | `TrickplayPreviewHttpSpecs.cs` (ComponentTests) |
| GET outcome mapping, conditional ETag comparison, Debug events | `PreviewOutcomeSpecs.cs` (UnitTests) |
| Probe outcome contract and Debug reasons | `TrickplayFrameProbeSpecs.cs` (UnitTests) |
| Authorization-split architectural boundary | `PreviewContextBoundarySpecs.cs` (UnitTests) |
| Resolution selection, frame selection, metadata validation | `TrickplayResolutionSelectorSpecs.cs`, `FrameSelectionSpecs.cs`, `TrickplayMetadataSpecs.cs` (UnitTests) |
| Disk cache behavior | `DiskPreviewCacheSpecs.cs` (ComponentTests) |
| Coordination, tree, and entry locks | `PreviewCacheCoordinationSpecs.cs`, `CacheTreeLockSpecs.cs`, `PreviewEntryLockRegistrySpecs.cs` (ComponentTests) |
| Encoder crop, failure, cancellation, decode permits | `TrickplayPreviewEncoderSpecs.cs` (ComponentTests) |
| Scheduled cleanup task | `ClearTrickplayCropperCacheTaskSpecs.cs` (UnitTests) |
| Release tools | `ManifestBuilderSpecs.cs`, `PackageValidatorSpecs.cs`, `ReleasePlannerSpecs.cs` (UnitTests) |
| Build-manifest, runtime, and dependency-lock contracts | `ReleaseContractSpecs.cs` (UnitTests) |
| Workflow contracts | `ReleaseWorkflowContractSpecs.cs`, `PublicationWorkflowContractSpecs.cs`, `ManifestWorkflowContractSpecs.cs` (UnitTests) |
| Harness smoke cases, scrub storm, report, deployment, input | `SmokeCasesSpecs.cs`, `ScrubStormSpecs.cs`, `ScrubStormReportSpecs.cs`, `DeploymentCycleSpecs.cs`, `HarnessInputSpecs.cs`, `HarnessHostOperationSpecs.cs`, `host_operation_specs.py` (ComponentTests) |
| Harness host gates and Debug-event reading | `LocalJellyfinSpecs.cs`, `DebugEventReaderSpecs.cs` (ComponentTests) |
| Plugin discovery, identity, and host activation | `PluginDiscoverySpecs.cs`, `PluginIdentitySpecs.cs` (UnitTests), `PluginHostActivationSpecs.cs` (ComponentTests) |
| Native Skia runtime | `SkiaRuntimeSpecs.cs` (ComponentTests) |
| Observation semantics without HTTP | `PreviewObservationSpecs.cs` (UnitTests) |

Repository-file contract tests load files through `RepositoryFiles` and parse workflow
YAML as text through `WorkflowFiles` (both in UnitTests).
