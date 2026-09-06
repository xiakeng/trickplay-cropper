# Caching map

Two observation caches answer *what did the last authoritative host read see*; the disk
cache turns one derived artifact into a Preview Cache Entry under the plugin-owned Cache
Tree. Preview Cache Coordination owns lease ordering (tree lease before entry lock) and
atomic publication; a Jellyfin scheduled task empties the tree. Request paths appear on
the [request paths map](request-paths.md).

## Preview Cache Coordination and cleanup

| Target | Key symbols | Responsibility |
|---|---|---|
| [DiskPreviewCache.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Caching/DiskPreviewCache.cs) | `GetOrCreateAsync`, `ClearAsync`, `GetFinalPath`, `EnsureRequestPathIsSafe` | Entry read-or-generate under coordination; temp-then-move publication; scheduled cleanup; path safety and reparse-point rejection |
| [PreviewCacheCoordination.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Caching/PreviewCacheCoordination.cs) | `ExecuteEntryAsync`, `AcquireExclusiveAsync`, `ExecuteCleanupEntryAsync` | Lease ordering and observable coordination boundaries |
| [CacheTreeLock.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Caching/CacheTreeLock.cs) | `AcquireSharedAsync`, `AcquireExclusiveAsync` | Writer-preferred reader/writer lock over the Cache Tree |
| [PreviewEntryLockRegistry.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Caching/PreviewEntryLockRegistry.cs) | `AcquireAsync` | Per-entry-path exclusive ownership with idle removal |
| [ClearTrickplayCropperCacheTask.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Tasks/ClearTrickplayCropperCacheTask.cs) | `ExecuteAsync` | Scheduled cleanup task; the only `IPreviewCacheMaintenance` consumer |
| [PreviewIdentity.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Preview/PreviewIdentity.cs) | `SourceStamp` | Source version stamp keying entries and ETags |

## Observation caches

Both caches share lifetimes (positive 30 minutes, explicit absence 5 minutes, measured
from authoritative read start), ordered publication via `ObservationStamp`, and
reclamation via `ReclaimingSourceCollection`. GET publishes only user-verified facts and
refreshes generated metadata unless a current absence applies; the probe reuses both
positive and scoped-absence observations.

| Target | Key symbols | Responsibility |
|---|---|---|
| [TrickplaySourceFactsCache.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/TrickplaySourceFactsCache.cs) | `GetForProbeAsync`, `BeginForPreview`, `PublishForPreview` | Source Facts Observation: user-independent Item membership and matched-source width |
| [TrickplayMetadataCache.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/TrickplayMetadataCache.cs) | `GetForProbeAsync`, `GetForPreviewAsync` | Generated Metadata Observation: per-width rows, whole-source absence, single-flight reads |
| [ReclaimingSourceCollection.cs](../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/ReclaimingSourceCollection.cs) | `GetOrAdd`, `TryRemove` | Retained-state container with periodic reclamation |

## Test entry points

Suite locations are on the [tests map](tests.md); each file below lives in the named
suite's directory.

| Behavior | Entry point |
|---|---|
| Entry ownership, publication, buffering, cleanup, path safety | `DiskPreviewCacheSpecs.cs` (ComponentTests) |
| Coordination boundary events | `PreviewCacheCoordinationSpecs.cs` (ComponentTests) |
| Tree and entry lock semantics | `CacheTreeLockSpecs.cs`, `PreviewEntryLockRegistrySpecs.cs` (ComponentTests) |
| Observation lifetimes and ordering over HTTP | `TrickplayPreviewHttpSpecs.cs` (ComponentTests) |
| Cache-tree snapshot invariants | `CacheTreeSnapshotSpecs.cs` (ComponentTests) |
| Reclamation | `ReclaimingSourceCollectionSpecs.cs` (UnitTests) |
| Scheduled task contract | `ClearTrickplayCropperCacheTaskSpecs.cs` (UnitTests) |
| Stamp and ETag identity | `PreviewIdentitySpecs.cs` (UnitTests) |
