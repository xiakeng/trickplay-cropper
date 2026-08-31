# Jellyfin Trickplay Lookup Contract

## Scope and source baseline

This note resolves the lookup contract for the first Trickplay Cropper release. The selected baseline is Jellyfin Server **10.11.11**, the current non-prerelease release when this research was performed, at source commit `1fbd8739292cce610231be93daf43368733edf63`. All source links below are pinned to that commit rather than to a moving branch. See the [official 10.11.11 release](https://github.com/jellyfin/jellyfin/releases/tag/v10.11.11).

## Decision

The plugin should use the following two `ITrickplayManager` methods exactly:

```csharp
Task<Dictionary<int, TrickplayInfo>> GetTrickplayResolutions(Guid itemId);

Task<string> GetTrickplayTilePathAsync(
    BaseItem item,
    int width,
    int index,
    bool saveWithMedia);
```

These are the published signatures in [`ITrickplayManager`](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Trickplay/ITrickplayManager.cs#L39-L44) and its [tile-path contract](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Trickplay/ITrickplayManager.cs#L76-L95). Neither method accepts a `CancellationToken` in 10.11.11.

The lookup sequence is:

1. Resolve `resolvedMediaSourceId = MediaSourceId ?? ItemId` as a `Guid` after the authenticated-user and media-source-membership checks.
2. Resolve the `BaseItem` whose `Id` is `resolvedMediaSourceId`. For an alternate version this must be the alternate version item, not the logical parent item.
3. Call `GetTrickplayResolutions(resolvedMediaSourceId)` and require an exact `TryGetValue(320, out info)` match. There is no nearest-width fallback.
4. Derive the frame, sprite, row, column, and crop rectangle from `info` as specified below.
5. Read `saveWithMedia` from `ILibraryManager.GetLibraryOptions(resolvedItem).SaveTrickplayWithMedia`.
6. Call `GetTrickplayTilePathAsync(resolvedItem, 320, spriteIndex, saveWithMedia)`. Treat an empty result or a missing file as unavailable. Do not call `GetTrickplayDirectory` and do not reconstruct any Jellyfin storage path.

This mirrors Jellyfin's own controller: it resolves the effective media-source item with the authenticated user, gets `SaveTrickplayWithMedia`, asks the manager for the path, and then checks file existence before returning it ([`TrickplayController.GetTrickplayTileImage`](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/TrickplayController.cs#L79-L103)). The manager performs its own width lookup and returns `string.Empty` when that width has no metadata; otherwise it returns the zero-based `<index>.jpg` path ([implementation](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L584-L594)).

## Metadata semantics

`GetTrickplayResolutions(Guid)` queries metadata by the supplied item id and builds a dictionary keyed by `TrickplayInfo.Width` ([implementation](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L494-L515)). For the fixed-width v1 contract, the fields mean:

| Plugin term | Jellyfin field | Type | Meaning |
| --- | --- | --- | --- |
| `frameWidth` | `Width` | `int` | Width in pixels of one thumbnail; it must be exactly `320` for v1. |
| `frameHeight` | `Height` | `int` | Height in pixels of one thumbnail. |
| `tileWidth` | `TileWidth` | `int` | Number of thumbnail columns in one sprite. |
| `tileHeight` | `TileHeight` | `int` | Number of thumbnail rows in one sprite. |
| `thumbnailCount` | `ThumbnailCount` | `int` | Total number of non-black thumbnails across all sprites, not the number of sprite JPEG files. |
| `intervalMs` | `Interval` | `int` | Milliseconds between thumbnails. |

These meanings are part of the [`TrickplayInfo` entity contract](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/TrickplayInfo.cs#L19-L65). The generation path sets `ThumbnailCount` from the count of extracted images, chunks those images into `TileWidth * TileHeight` groups, and creates `ceil(imageCount / framesPerSprite)` files ([tile creation](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L392-L435)). Jellyfin's HLS builder independently derives the same sprite count from `ThumbnailCount`, `TileWidth`, and `TileHeight` ([playlist calculation](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L597-L625)).

The plugin should reject unusable metadata before division or crop arithmetic: `Width != 320`, `Height <= 0`, `Interval <= 0`, `TileWidth <= 0`, `TileHeight <= 0`, or `ThumbnailCount <= 0`. Such metadata cannot identify a valid v1 preview. Arithmetic should be performed in `long` (and checked when converting back to `int`) so malformed metadata cannot overflow intermediate products.

## Frame and sprite calculation

With `PositionTicks >= 0`, the safe calculation is equivalent to:

```csharp
long ticksPerFrame = checked((long)info.Interval * TimeSpan.TicksPerMillisecond);
long rawFrameIndex = positionTicks / ticksPerFrame;
int frameIndex = checked((int)Math.Min(rawFrameIndex, (long)info.ThumbnailCount - 1));

long framesPerSprite = checked((long)info.TileWidth * info.TileHeight);
int spriteIndex = checked((int)(frameIndex / framesPerSprite));
long cellIndex = frameIndex % framesPerSprite;
int row = checked((int)(cellIndex / info.TileWidth));
int column = checked((int)(cellIndex % info.TileWidth));

int cropX = checked(column * info.Width);
int cropY = checked(row * info.Height);
int cropWidth = info.Width;
int cropHeight = info.Height;
```

Sprites and cells are zero-based. Jellyfin writes sprite files as `0.jpg`, `1.jpg`, and so on, and divides the ordered image sequence into consecutive sprite-sized chunks ([file loop](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L423-L432)). Within each sprite, Jellyfin draws rows from top to bottom and columns from left to right while incrementing the image index ([Skia encoder](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/src/Jellyfin.Drawing.Skia/SkiaEncoder.cs#L742-L775)). The requirement's row-major formula therefore matches Jellyfin 10.11.11.

`GetTrickplayTilePathAsync` does not validate that `index` is in range and does not check whether the file exists. The plugin must derive the index from validated metadata, then perform its own `File.Exists` check, as Jellyfin's controller does.

## Media-source identifier details

The request's `Guid` types are compatible with Jellyfin's supported local trickplay sources. `ITrickplayManager` accepts a `Guid`, and Jellyfin's official trickplay endpoints also accept `Guid itemId` plus optional `Guid? mediaSourceId` ([controller signatures](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/TrickplayController.cs#L50-L59)).

There is one representation detail: `MediaSourceInfo.Id` is a `string` ([model](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Dto/MediaSourceInfo.cs#L14-L52)), although Jellyfin creates local media-source ids from the backing `BaseItem.Id` in lowercase `N` GUID format ([source construction](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Entities/BaseItem.cs#L1125-L1155)). Membership checks should therefore use `Guid.TryParse(source.Id, out var id)` and compare GUID values, rather than depend on string formatting. Jellyfin's own manifest code skips remote sources and non-GUID source ids before calling `GetTrickplayResolutions` ([manifest implementation](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L562-L581)).

Alternate versions are separate backing items: a video's media-source list begins with the video itself and then includes linked/local alternate `BaseItem` instances ([video source enumeration](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Entities/Video.cs#L533-L563)). This is why metadata lookup and tile-path lookup must both use the resolved media-source item id, while the logical route item is retained for the membership and authorization boundary.

## Requirement mismatches and caveats

1. **The removed `Width` query parameter is correct for v1.** Width is now a fixed internal constant of `320`; absence of the exact dictionary key means `404`, even when another width exists. Jellyfin may generate a width smaller than configured for source videos narrower than the configured target, so those items legitimately have no `320` entry ([generation width adjustment](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L264-L276)).
2. **`ThumbnailCount` is a frame count, not a sprite count.** The v1 formulas are correct only with that semantic. Sprite count is `ceil(ThumbnailCount / (TileWidth * TileHeight))`.
3. **Jellyfin 10.11.11 has an upstream import-path inconsistency.** When tile files already exist but their database metadata does not, the import path writes `ThumbnailCount = existingFiles.Length`, which is the sprite-file count, despite the entity and normal-generation paths defining it as the total thumbnail count ([import path](https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L278-L312)). The plugin must not guess a replacement count from storage. It should trust Jellyfin metadata, log enough metadata to diagnose early clamping, and document regeneration of Jellyfin trickplay data as the recovery when this upstream state is encountered.
4. **The path method needs more than the id.** It requires the resolved `BaseItem` and the correct `SaveTrickplayWithMedia` value. Passing the logical item for an alternate version can query or construct the wrong path.

## Implementation contract to carry forward

- Fixed v1 width: `320`; no public `Width` parameter and no closest-resolution fallback.
- Metadata source: exact `GetTrickplayResolutions(resolvedMediaSourceId)[320]` lookup.
- Sprite path source: only `GetTrickplayTilePathAsync(resolvedItem, 320, spriteIndex, saveWithMedia)`.
- Frame count source: `ThumbnailCount`; sprite count is derived, never substituted for it.
- Layout: zero-based, row-major; `TileWidth` is columns and `TileHeight` is rows.
- Missing width, empty path, or missing sprite file: `404` under the v1 HTTP contract.
- Invalid metadata: fail before arithmetic or decode and log the metadata; do not infer storage or silently choose a different resolution.
