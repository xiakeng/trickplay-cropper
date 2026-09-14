# Tests map

[UnitTests](../../tests/Jellyfin.Plugin.TrickplayCropper.UnitTests) proves pure contracts;
[ComponentTests](../../tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests) proves
server, filesystem, native-runtime, and external-tool behavior. Both run in CI; the
live host remains the manual harness ([tooling map](tooling.md)).

Each file below lives in the named suite's directory.

| To prove or change… | Entry point |
|---|---|
| GET response and authorization behavior | `TrickplayPreviewGetResponseHttpSpecs.cs`, `TrickplayPreviewAuthorizationHttpSpecs.cs`, `TrickplayPreviewFailureHttpSpecs.cs` (ComponentTests) |
| Frame Timeline HTTP contract, source binding, and no-image work | `TrickplayFrameTimelineHttpSpecs.cs` (ComponentTests) |
| HTTP host, request, fake, authentication, scenario, metadata, source, and assertion support | `PreviewHttpHostFixture.cs`, `PreviewHttpRequests.cs`, `PreviewHttpJellyfinFakes.cs`, `PreviewHttpAuthentication.cs`, `PreviewHttpScenario.cs`, `MetadataReadPlan.cs`, `SourceReadPlan.cs`, `TrickplayPreviewHttpSupport.cs` (ComponentTests) |
| GET outcome mapping, conditional ETag comparison, Debug events | `PreviewOutcomeSpecs.cs` (UnitTests) |
| Resolution selection, frame selection, metadata, and identity validation | `TrickplayResolutionSelectorSpecs.cs`, `FrameSelectionSpecs.cs`, `PreviewIdentitySpecs.cs` (UnitTests) |
| Disk cache entry, path, cleanup, and failure behavior | `DiskPreviewCacheEntrySpecs.cs`, `DiskPreviewCachePathSafetySpecs.cs`, `DiskPreviewCacheCleanupEligibilitySpecs.cs`, `DiskPreviewCacheCleanupCoordinationSpecs.cs`, `DiskPreviewCacheFailureSpecs.cs`, `DiskPreviewCacheSupport.cs` (ComponentTests) |
| Coordination, tree, and entry locks | `PreviewCacheCoordinationSpecs.cs`, `CacheTreeLockSpecs.cs`, `PreviewEntryLockRegistrySpecs.cs` (ComponentTests) |
| Encoder crop, input, and failure behavior | `TrickplayPreviewEncoderCropSpecs.cs`, `TrickplayPreviewEncoderSupport.cs` (ComponentTests) |
| Encoder cancellation, decode permits, and telemetry | `TrickplayPreviewEncoderConcurrencySpecs.cs` (ComponentTests) |
| Scheduled cleanup task | `ClearTrickplayCropperCacheTaskSpecs.cs` (UnitTests) |
| Release tools | `ManifestBuilderSpecs.cs`, `PackageValidatorSpecs.cs`, `ReleasePlannerSpecs.cs` (UnitTests) |
| Build-manifest, runtime, and dependency-lock contracts | `ReleaseContractSpecs.cs` (UnitTests) |
| Repository line/script and Code Map token-limit contracts | `RepositoryStructureContractSpecs.cs` (ComponentTests) |
| Harness smoke cases, direct-index scrub storm, deployment, input, and host operation | `SmokeCasesSpecs.cs`, `ScrubStormSpecs.cs`, `DeploymentCycleSpecs.cs`, `HarnessInputSpecs.cs`, `HarnessHostOperationSpecs.cs`, `host_operation_specs.py` (ComponentTests) |
| Harness host gates and Debug-event reading | `LocalJellyfinSpecs.cs`, `DebugEventReaderSpecs.cs` (ComponentTests) |
| Plugin discovery, identity, and host activation | `PluginDiscoverySpecs.cs`, `PluginIdentitySpecs.cs` (UnitTests), `PluginHostActivationSpecs.cs` (ComponentTests) |
| Native Skia runtime | `SkiaRuntimeSpecs.cs` (ComponentTests) |
