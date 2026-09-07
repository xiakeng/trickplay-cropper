# Observation caches map

Source Facts and Generated Metadata Observations have independent freshness,
publication, and reclamation. Ordered publication uses read-start stamps; GET publishes
only user-verified source facts.

| Target | Key symbols | Responsibility |
|---|---|---|
| [TrickplaySourceFactsCache.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/TrickplaySourceFactsCache.cs) | `GetForProbeAsync`, `BeginForPreview`, `PublishForPreview` | Item membership and matched-source width |
| [TrickplayMetadataCache.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/TrickplayMetadataCache.cs) | `GetForProbeAsync`, `GetForPreviewAsync` | Host reads and Generated Metadata resolution |
| [GeneratedMetadataObservationState.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/GeneratedMetadataObservationState.cs) | `ResolveOrReserve`, `Publish`, `Reject`, `Complete`, `TryRetire` | Per-source ordered rows, coverage, settlement, freshness, reclamation |
| [ReclaimingSourceCollection.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/ReclaimingSourceCollection.cs) | `GetOrAdd`, `TryRemove` | Retained-state container and periodic reclamation |

## Test entry points

| Behavior | Entry point |
|---|---|
| Lifetimes and source ordering over HTTP | `TrickplaySourceFactsObservationHttpSpecs.cs` (ComponentTests) |
| Metadata ordering and scopes over HTTP | `TrickplayGeneratedMetadataObservationHttpSpecs.cs`, `TrickplayGeneratedMetadataScopeHttpSpecs.cs` (ComponentTests) |
| Reclamation | `ReclaimingSourceCollectionSpecs.cs` (UnitTests) |
| Stamp and ETag identity | `PreviewIdentitySpecs.cs` (UnitTests) |
