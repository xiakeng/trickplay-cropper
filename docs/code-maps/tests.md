# Tests map

[UnitTests](../../tests/Jellyfin.Plugin.TrickplayCropper.UnitTests) proves pure contracts;
[ComponentTests](../../tests/Jellyfin.Plugin.TrickplayCropper.ComponentTests) proves
server, filesystem, native-runtime, and external-tool behavior. Both run in CI; the
live host remains the manual harness ([tooling map](tooling.md)).

Each file below lives in the named suite's directory.

| To prove or change… | Entry point |
|---|---|
| Preview GET response, authorization, failure, and conditional behavior | `TrickplayPreviewGetResponseHttpSpecs.cs`, `TrickplayPreviewAuthorizationHttpSpecs.cs`, `TrickplayPreviewFailureHttpSpecs.cs` (ComponentTests) |
| Frame Timeline HTTP behavior and real Kestrel | `TrickplayFrameTimelineHttpSpecs.cs`, `TrickplayPreviewKestrelHttpSpecs.cs` (ComponentTests) |
| HTTP host, request, fake, authentication, scenario, and assertion support | `PreviewHttpHostFixture.cs`, `PreviewHttpRequests.cs`, `PreviewHttpJellyfinFakes.cs`, `PreviewHttpAuthentication.cs`, `PreviewHttpScenario.cs`, `TrickplayPreviewHttpSupport.cs` (ComponentTests) |
| Preview outcomes, identity, resolution, and frame selection | `PreviewOutcomeSpecs.cs`, `PreviewIdentitySpecs.cs`, `TrickplayResolutionSelectorSpecs.cs`, `FrameSelectionSpecs.cs` (UnitTests) |
| Disk cache entry, path, cleanup, and failure behavior | `DiskPreviewCacheEntrySpecs.cs`, `DiskPreviewCachePathSafetySpecs.cs`, `DiskPreviewCacheCleanupEligibilitySpecs.cs`, `DiskPreviewCacheCleanupCoordinationSpecs.cs`, `DiskPreviewCacheFailureSpecs.cs`, `DiskPreviewCacheSupport.cs` (ComponentTests) |
| Coordination, tree, and entry locks | `PreviewCacheCoordinationSpecs.cs`, `CacheTreeLockSpecs.cs`, `CacheTreeSnapshotSpecs.cs`, `PreviewEntryLockRegistrySpecs.cs` (ComponentTests) |
| Encoder crop, input, cancellation, decode permits, and telemetry | `TrickplayPreviewEncoderCropSpecs.cs`, `TrickplayPreviewEncoderConcurrencySpecs.cs`, `TrickplayPreviewEncoderSupport.cs` (ComponentTests) |
| Scheduled cleanup task | `ClearTrickplayCropperCacheTaskSpecs.cs` (UnitTests) |
| Release tools and contracts | `ManifestBuilderSpecs.cs`, `PackageValidatorSpecs.cs`, `ReleasePlannerSpecs.cs`, `ReleaseContractSpecs.cs` (UnitTests) |
| Repository structure and Code Map size contracts | `RepositoryStructureContractSpecs.cs` (ComponentTests) |
| Harness smoke cases, direct-index scrub storm, deployment, and input | `SmokeCasesSpecs.cs`, `ScrubStormSpecs.cs`, `DeploymentCycleSpecs.cs`, `HarnessInputSpecs.cs`, `HarnessHostOperationSpecs.cs`, `host_operation_specs.py` (ComponentTests) |
| Harness host gates and Debug-event reading | `LocalJellyfinSpecs.cs`, `DebugEventReaderSpecs.cs` (ComponentTests) |
| Plugin discovery, identity, and host activation | `PluginDiscoverySpecs.cs`, `PluginIdentitySpecs.cs` (UnitTests), `PluginHostActivationSpecs.cs` (ComponentTests) |
| Native Skia runtime and host application wiring | `SkiaRuntimeSpecs.cs`, `ServerApplicationHostSpecs.cs` (ComponentTests) |
