# Jellyfin 10.11.11 Trickplay Resolution Contract

## Scope and sources

This note answers the resolution-contract questions for Jellyfin Server
10.11.11. The repository pins `Jellyfin.Controller` and `Jellyfin.Model` to that
version. [Repository package pins][package-pins] The source links below are fixed
to commit `1fbd8739292cce610231be93daf43368733edf63`, which is the source commit for
the official `v10.11.11` tag. [Jellyfin release tag][jellyfin-release-tag]
[Jellyfin source commit][jellyfin-source-commit]

## Answer

Jellyfin does **not** expose one source-specific Trickplay Resolution Target for
a selected Source Video. It exposes two different kinds of state:

1. `ServerConfiguration.TrickplayOptions.WidthResolutions` is a server-wide
   array of Trickplay Resolution Targets. Its default is `[320]`. It is not a
   per-library or per-item value. [Server configuration][server-configuration]
   [Trickplay options][trickplay-options]
2. `ITrickplayManager.GetTrickplayResolutions(sourceVideoId)` returns the
   generated metadata currently recorded for one Source Video, keyed by its
   recorded `TrickplayInfo.Width`. This dictionary, not the configuration
   array, is the authority for recorded resolution metadata. It does not prove
   that a tile file currently exists; GET establishes servability by resolving
   and validating the selected tile.
   [Manager interface][manager-interface] [Manager implementation][resolution-query]

Per-library options only control whether generation is enabled, whether it runs
during scans, and whether tiles are saved with the media. They do not select a
width. An in-process plugin reads those options for the authorized effective
Source Video through `ILibraryManager.GetLibraryOptions(sourceVideo)`.
`SaveTrickplayWithMedia` must then be passed to
`ITrickplayManager.GetTrickplayTilePathAsync`; it selects Jellyfin's storage root
without requiring the plugin to derive a path. [Library manager][library-manager]
[Library options][library-options] [Manager interface][manager-interface]

Consequently, replacing the plugin's hard-coded `320` requires an explicit
selection policy over current **Trickplay Resolution Targets** and the Source
Video's recorded resolution metadata. "Read the Jellyfin setting" alone is
insufficient when more than one target is present. An implementation must define
how it derives one Selected Trickplay Resolution because Jellyfin does not choose
one for the plugin.

## Public interfaces

In an in-process plugin, the supported interfaces are:

- `IServerConfigurationManager.Configuration.TrickplayOptions.WidthResolutions`
  for the current server-wide generation targets.
  [Configuration manager][configuration-manager]
- `ITrickplayManager.GetTrickplayResolutions(Guid)` for the selected Source
  Video's recorded resolutions, and `GetTrickplayManifest(BaseItem)` when a
  logical item's local Media Sources must be grouped first.
  [Manager interface][manager-interface]
- `ILibraryManager.GetLibraryOptions(BaseItem)` for the effective Source Video's
  `SaveTrickplayWithMedia` value. [Library manager][library-manager]
- `ITrickplayManager.GetTrickplayTilePathAsync(...)` for resolving a tile after
  metadata for the Selected Trickplay Resolution has been identified. The plugin
  must pass `SaveTrickplayWithMedia` and use this interface instead of deriving
  Jellyfin's storage layout. [Manager interface][manager-interface]

Jellyfin also exposes the server configuration at authenticated
`GET /System/Configuration`, and exposes the generated manifest on
`BaseItemDto.Trickplay` when `ItemFields.Trickplay` is requested. Those HTTP/DTO
surfaces describe the same state; an in-process plugin does not need to call
back into HTTP. [Configuration controller][configuration-controller]
[DTO projection][dto-projection]

## Generation and normalization

On refresh, Jellyfin reads the current global `TrickplayOptions`, then iterates
every Trickplay Resolution Target in `WidthResolutions`. Before generation it
normalizes the requested width:

- odd widths are rounded down to an even number;
- a requested width larger than the Source Video width is clamped to the
  Source Video width, also rounded down to even.

Freshly generated `TrickplayInfo.Width` and the dictionary key therefore contain
the source-specific recorded width, which can differ from the originating
Trickplay Resolution Target. After the product chooses a target, its normalized
even width is the Selected Trickplay Resolution.
[Refresh loop][refresh-loop] [Width normalization][width-normalization]
[Generated metadata][generated-metadata]

The generated metadata is stored per `(ItemId, Width)`. Its interval, tile
geometry, thumbnail count, height, and bandwidth are snapshots of generation
time, not live projections of the current configuration.
[Database key][database-key] [Trickplay entity][trickplay-entity]

## Alternate Media Sources

Jellyfin models local alternate versions as their own Source Video/item. During
generation, it deliberately selects the Media Source whose GUID equals that
Source Video's item ID. `GetTrickplayManifest` likewise queries each local,
GUID-valued Media Source ID independently and omits remote or non-GUID sources.
[Source generation][source-generation] [Manifest construction][manifest-construction]

The plugin must therefore resolve and authorize the requested logical item and
selected Media Source, then query Trickplay metadata with the effective Source
Video GUID. It must not use the logical item ID for an alternate source or fall
back between versions.

## Disabled generation and configuration changes

The Trickplay Resolution Target array and the generated dictionary are
intentionally not kept in lockstep:

- When library Trickplay extraction is disabled and a `replace: false` refresh
  reaches an otherwise eligible video, Jellyfin deletes that video's Trickplay
  directory and database rows, then returns. Until such a refresh runs,
  previously generated metadata can still exist. With `replace: true`, the same
  branch deletes the old data but continues and regenerates from the current
  global options despite the disabled flag. [Disabled refresh][disabled-refresh]
- The scheduled generation task calls refresh with `replace: false` for library
  videos. [Scheduled refresh][scheduled-refresh]
- With `replace: false`, Jellyfin reuses an existing row only when the output
  directory for the current width, tile grid, and storage location exists,
  contains at least one file, and the database contains that Source Video/width
  row. If the directory is absent or empty, Jellyfin regenerates. If files exist
  but the row is absent, it imports metadata from those files and returns. That
  10.11.11 import path locates the directory with the normalized `actualWidth`
  but writes the raw Trickplay Resolution Target into `TrickplayInfo.Width`. An
  odd or oversized target can therefore create a dictionary key that differs
  from the Selected Trickplay Resolution even though the tiles exist; the
  approved exact-match/no-fallback policy returns `404` for that mismatch.
  [Existing-tile import][existing-tile-import] [Selection policy][selection-policy]
  Removing a target from `WidthResolutions` does not delete its database row,
  and cleanup treats every existing row as expected. Adding a target can generate
  a new row. Changes such as interval or JPEG quality therefore leave existing
  data unchanged only when those reuse prerequisites hold. A tile-grid change
  changes the directory identity and can regenerate and replace the row.
  [Existing-width reuse][existing-width-reuse] [Refresh cleanup][refresh-cleanup]
- A full metadata refresh with Trickplay regeneration requested passes
  `replace: true`; this deletes all recorded data before regenerating from the
  current options. [Regeneration provider][regeneration-provider]
- An empty target array performs no generation and does not itself remove old
  rows. An item that fails Jellyfin's generation-eligibility check returns
  before disabled-library cleanup. [Refresh loop][refresh-loop]
  [Eligibility check][eligibility-check]

Thus neither "configured now" nor "generation enabled now" proves that a
resolution exists, and a resolution absent from the current target array can
still be valid recorded data.

## Failure distinctions required in the plugin

The plugin should keep the following outcomes distinct and fail closed:

The HTTP mappings below come from the approved selection policy. The Source
Sprite availability boundary reflects the current resolver and encoder behavior
and the repository's source-trust ADR. [Selection policy][selection-policy]
[Source resolver][source-resolver] [Source encoder][source-encoder]
[Source Sprite ADR][source-sprite-adr]

| Outcome | Meaning | Handling boundary |
| --- | --- | --- |
| Configuration unavailable | `TrickplayOptions` or its target array is null or cannot be read safely | Internal/configuration failure (`500`); do not substitute `320` |
| Structurally invalid target configuration | Any target is non-positive, or normalization produces a non-positive Selected Trickplay Resolution; duplicate positive targets remain valid | Internal/configuration failure (`500`) |
| No Trickplay Resolution Target | The array is empty | Jellyfin performs no generation and can retain old rows; the approved product policy independently maps the request to `404` |
| Multiple Trickplay Resolution Targets | Jellyfin exposes several generation targets | Not an error by itself; the approved product policy selects the minimum target |
| Selected Source Video is absent, remote, non-GUID, not a member of the logical item, or unauthorized | The request does not identify an allowed local Source Video | Preserve the API's authorization/not-found concealment policy |
| No generated metadata for the effective Source Video | `GetTrickplayResolutions` is empty | Not found/not ready; do not inspect directories |
| No exact Selected Trickplay Resolution entry | Generated metadata exists, but none matches the approved selection policy | Not found/configuration mismatch; log the targets and generated keys |
| No available frames | `ThumbnailCount` is non-positive | No preview is available (`404`, `NoThumbnails`) |
| Invalid metadata for the Selected Trickplay Resolution | Key/`Width` disagreement, or non-positive height, interval, tile width, or tile height | Invalid Jellyfin metadata (`500`); do not calculate a frame or guess another width |
| Tile path empty or file absent when GET validates it | Metadata exists but the selected Source Sprite is unavailable at the observable availability boundary | GET source-resolution failure (`404`); resolve through `ITrickplayManager`, never by path convention |
| A later Source Sprite stat, filesystem, snapshot, decode, or encode operation throws | The file disappeared, changed incompatibly, or otherwise failed after availability validation | Operational failure (`500`), not a normal not-found outcome; an undetected replacement can still be served |
| Jellyfin manager or database operation throws | Availability could not be determined | Operational failure (`500`), not a normal not-found outcome |

A raw equality test between a Trickplay Resolution Target and a generated
dictionary key is not universally valid: Jellyfin's even-width normalization and
source-width clamp can produce a different legitimate key. The approved product
policy applies that normalization, selects the minimum target, and requires one
exact Selected Trickplay Resolution match without fallback. [Selection
policy][selection-policy]

Under the separately approved lightweight HEAD contract, HEAD stops after
authorization, selection, metadata validation, and Frame Index calculation; GET
continues through Source Sprite resolution and inspection. That operation split
comes from the HEAD contract, not from Jellyfin's resolution model established by
this note. [HEAD contract][head-contract]

[package-pins]: ../../Directory.Packages.props
[jellyfin-release-tag]: https://github.com/jellyfin/jellyfin/releases/tag/v10.11.11
[jellyfin-source-commit]: https://github.com/jellyfin/jellyfin/commit/1fbd8739292cce610231be93daf43368733edf63
[server-configuration]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Configuration/ServerConfiguration.cs#L281-L285
[trickplay-options]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Configuration/TrickplayOptions.cs#L37-L55
[library-options]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Configuration/LibraryOptions.cs#L28-L118
[library-manager]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Library/ILibraryManager.cs#L492-L496
[configuration-manager]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Configuration/IServerConfigurationManager.cs#L7-L22
[manager-interface]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Trickplay/ITrickplayManager.cs#L39-L95
[resolution-query]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L494-L515
[configuration-controller]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Api/Controllers/ConfigurationController.cs#L23-L69
[dto-projection]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Emby.Server.Implementations/Dto/DtoService.cs#L1131-L1138
[refresh-loop]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L138-L196
[width-normalization]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L239-L276
[generated-metadata]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L319-L359
[database-key]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/src/Jellyfin.Database/Jellyfin.Database.Implementations/ModelConfiguration/TrickplayInfoConfiguration.cs#L10-L16
[trickplay-entity]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/TrickplayInfo.cs#L6-L73
[source-generation]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L239-L253
[manifest-construction]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L562-L581
[disabled-refresh]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L138-L175
[scheduled-refresh]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Providers/Trickplay/TrickplayImagesTask.cs#L80-L111
[existing-width-reuse]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L278-L317
[existing-tile-import]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L264-L315
[refresh-cleanup]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L198-L220
[regeneration-provider]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Providers/Trickplay/TrickplayProvider.cs#L96-L116
[eligibility-check]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L463-L491
[selection-policy]: https://github.com/xiakeng/trickplay-cropper/issues/49#issuecomment-5507410034
[head-contract]: ./head-endpoint-contract.md
[source-resolver]: ../../src/Jellyfin.Plugin.TrickplayCropper/Jellyfin/JellyfinPreviewSourceResolver.cs
[source-encoder]: ../../src/Jellyfin.Plugin.TrickplayCropper/Imaging/TrickplayPreviewEncoder.cs
[source-sprite-adr]: ../adr/0002-trust-jellyfin-source-sprites-without-plugin-caps.md
