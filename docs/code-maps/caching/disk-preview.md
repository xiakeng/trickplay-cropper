# Disk Preview cache map

Preview Cache Coordination owns lease ordering (tree before entry) and atomic
publication. Request handling, cleanup, and path safety are separate modules.

| Target | Key symbols | Responsibility |
|---|---|---|
| [DiskPreviewCache.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Caching/DiskPreviewCache.cs) | `GetOrCreateAsync`, `ClearAsync` | Stable request and maintenance boundary |
| [DiskPreviewCacheEntryStore.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Caching/DiskPreviewCacheEntryStore.cs) | `GetOrCreateAsync` | Entry read, generation, atomic publication, winner handling, buffering |
| [DiskPreviewCacheCleanup.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Caching/DiskPreviewCacheCleanup.cs) | `ClearAsync` | Serialized traversal, candidate ownership, pruning, cancellation, summary |
| [PreviewCachePaths.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Caching/PreviewCachePaths.cs) | `GetFinalPath`, `EnsureRequestPathIsSafe`, `IsReparsePoint` | Shared containment and reparse-point safety |
| [PreviewCacheCoordination.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Caching/PreviewCacheCoordination.cs) | `ExecuteEntryAsync`, `AcquireExclusiveAsync`, `ExecuteCleanupEntryAsync` | Lease ordering and observable checkpoints |
| [CacheTreeLock.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Caching/CacheTreeLock.cs) | `AcquireSharedAsync`, `AcquireExclusiveAsync` | Writer-preferred Cache Tree lock |
| [PreviewEntryLockRegistry.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Caching/PreviewEntryLockRegistry.cs) | `AcquireAsync` | Per-entry ownership and idle removal |
| [ClearTrickplayCropperCacheTask.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Tasks/ClearTrickplayCropperCacheTask.cs) | `ExecuteAsync` | Scheduled maintenance consumer |
| [PreviewIdentity.cs](../../../src/Jellyfin.Plugin.TrickplayCropper/Preview/PreviewIdentity.cs) | `SourceStamp` | Entry and ETag identity |

## Test entry points

| Behavior | ComponentTests entry point |
|---|---|
| Entry ownership, publication, buffering | `DiskPreviewCacheEntrySpecs.cs` |
| Path safety | `DiskPreviewCachePathSafetySpecs.cs` |
| Cleanup eligibility and coordination | `DiskPreviewCacheCleanupEligibilitySpecs.cs`, `DiskPreviewCacheCleanupCoordinationSpecs.cs` |
| Entry and cleanup failures | `DiskPreviewCacheFailureSpecs.cs` |
| Coordination and locks | `PreviewCacheCoordinationSpecs.cs`, `CacheTreeLockSpecs.cs`, `PreviewEntryLockRegistrySpecs.cs` |
| Tree snapshot invariants | `CacheTreeSnapshotSpecs.cs` |
