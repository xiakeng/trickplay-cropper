# Tests map

[UnitTests](../../tests/Jellyfin.Plugin.TrickplayCropper.UnitTests) proves pure contracts;
[ComponentTests](../../tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests) proves
server, filesystem, native-runtime, and external-tool behavior. Both run in CI; the
live host remains the manual harness ([tooling map](tooling.md)).

Each file below lives in the named suite's directory.

| To prove or change… | Entry point |
|---|---|
| GET response and authorization behavior | `TrickplayPreviewGetResponseHttpSpecs.cs`, `TrickplayPreviewAuthorizationHttpSpecs.cs`, `TrickplayPreviewFailureHttpSpecs.cs` (ComponentTests) |
| Probe HTTP behavior and real Kestrel | `TrickplayFrameProbeHttpSpecs.cs`, `TrickplayPreviewKestrelHttpSpecs.cs` (ComponentTests) |
| Source Facts Observation behavior | `TrickplaySourceFactsObservationHttpSpecs.cs` (ComponentTests) |
| Generated Metadata Observation behavior | `TrickplayGeneratedMetadataObservationHttpSpecs.cs`, `TrickplayGeneratedMetadataScopeHttpSpecs.cs` (ComponentTests) |
| HTTP host, request, fake, authentication, scenario, observation, and assertion support | `PreviewHttpHostFixture.cs`, `PreviewHttpRequests.cs`, `PreviewHttpJellyfinFakes.cs`, `PreviewHttpAuthentication.cs`, `PreviewHttpScenario.cs`, `MetadataReadPlan.cs`, `SourceReadPlan.cs`, `TrickplayPreviewHttpSupport.cs` (ComponentTests) |
| GET outcome mapping, conditional ETag comparison, Debug events | `PreviewOutcomeSpecs.cs` (UnitTests) |
| Probe outcome contract and Debug reasons | `TrickplayFrameProbeSpecs.cs` (UnitTests) |
| Authorization-split architectural boundary | `PreviewContextBoundarySpecs.cs` (UnitTests) |
| Resolution selection, frame selection, metadata validation | `TrickplayResolutionSelectorSpecs.cs`, `FrameSelectionSpecs.cs`, `TrickplayMetadataSpecs.cs` (UnitTests) |
| Disk cache entry, path, cleanup, and failure behavior | `DiskPreviewCacheEntrySpecs.cs`, `DiskPreviewCachePathSafetySpecs.cs`, `DiskPreviewCacheCleanupEligibilitySpecs.cs`, `DiskPreviewCacheCleanupCoordinationSpecs.cs`, `DiskPreviewCacheFailureSpecs.cs`, `DiskPreviewCacheSupport.cs` (ComponentTests) |
| Coordination, tree, and entry locks | `PreviewCacheCoordinationSpecs.cs`, `CacheTreeLockSpecs.cs`, `PreviewEntryLockRegistrySpecs.cs` (ComponentTests) |
| Encoder crop, input, and failure behavior | `TrickplayPreviewEncoderCropSpecs.cs`, `TrickplayPreviewEncoderSupport.cs` (ComponentTests) |
| Encoder cancellation, decode permits, and telemetry | `TrickplayPreviewEncoderConcurrencySpecs.cs` (ComponentTests) |
| Scheduled cleanup task | `ClearTrickplayCropperCacheTaskSpecs.cs` (UnitTests) |
| Release tools | `ManifestBuilderSpecs.cs`, `PackageValidatorSpecs.cs`, `ReleasePlannerSpecs.cs` (UnitTests) |
| Build-manifest, runtime, and dependency-lock contracts | `ReleaseContractSpecs.cs` (UnitTests) |
| Workflow contracts | `ReleaseWorkflowContractSpecs.cs`, `PublicationWorkflowContractSpecs.cs`, `ManifestWorkflowContractSpecs.cs` (UnitTests) |
| Repository line/script and Code Map token/link failure contracts | `RepositoryStructureContractSpecs.cs` (ComponentTests) |
| Harness smoke cases, scrub storm, report, deployment, input | `SmokeCasesSpecs.cs`, `ScrubStormSpecs.cs`, `ScrubStormReportSpecs.cs`, `DeploymentCycleSpecs.cs`, `HarnessInputSpecs.cs`, `HarnessHostOperationSpecs.cs`, `host_operation_specs.py` (ComponentTests) |
| Harness host gates and Debug-event reading | `LocalJellyfinSpecs.cs`, `DebugEventReaderSpecs.cs` (ComponentTests) |
| Plugin discovery, identity, and host activation | `PluginDiscoverySpecs.cs`, `PluginIdentitySpecs.cs` (UnitTests), `PluginHostActivationSpecs.cs` (ComponentTests) |
| Native Skia runtime | `SkiaRuntimeSpecs.cs` (ComponentTests) |
| Observation semantics without HTTP | `PreviewObservationSpecs.cs` (UnitTests) |

Repository-file contract tests load files through `RepositoryFiles` and parse workflow
YAML as text through `WorkflowFiles` (both in UnitTests).
