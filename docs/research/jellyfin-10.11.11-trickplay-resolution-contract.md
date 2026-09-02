# Jellyfin 10.11.11 Trickplay Resolution Contract

## Scope and sources

This note answers the resolution-contract questions for Jellyfin Server
10.11.11. The repository pins `Jellyfin.Controller` and `Jellyfin.Model` to that
version, and the source links below are fixed to the commit tagged `v10.11.11`
(`1fbd8739292cce610231be93daf43368733edf63`).

## Answer

Jellyfin does **not** expose one configured Trickplay Resolution for a selected
Source Video. It exposes two different kinds of state:

1. `ServerConfiguration.TrickplayOptions.WidthResolutions` is a server-wide
   array of requested generation widths. Its default is `[320]`. It is not a
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
width. [Library options][library-options]

Consequently, replacing the plugin's hard-coded `320` requires an explicit
selection policy over **configured targets and generated source metadata**.
"Read the Jellyfin setting" alone is insufficient when more than one width is
configured. An implementation must not invent a single effective width that
Jellyfin itself does not define.

## Public interfaces

In an in-process plugin, the supported interfaces are:

- `IServerConfigurationManager.Configuration.TrickplayOptions.WidthResolutions`
  for the current server-wide generation targets.
  [Configuration manager][configuration-manager]
- `ITrickplayManager.GetTrickplayResolutions(Guid)` for the selected Source
  Video's recorded resolutions, and `GetTrickplayManifest(BaseItem)` when a
  logical item's local Media Sources must be grouped first.
  [Manager interface][manager-interface]
- `ITrickplayManager.GetTrickplayTilePathAsync(...)` for resolving a tile after
  a generated resolution has been selected. The plugin must use this instead of
  deriving Jellyfin's storage layout. [Manager interface][manager-interface]

Jellyfin also exposes the server configuration at authenticated
`GET /System/Configuration`, and exposes the generated manifest on
`BaseItemDto.Trickplay` when `ItemFields.Trickplay` is requested. Those HTTP/DTO
surfaces describe the same state; an in-process plugin does not need to call
back into HTTP. [Configuration controller][configuration-controller]
[DTO projection][dto-projection]

## Generation and normalization

On refresh, Jellyfin reads the current global `TrickplayOptions`, then iterates
every value in `WidthResolutions`. Before generation it normalizes the requested
width:

- odd widths are rounded down to an even number;
- a requested width larger than the Source Video width is clamped to the
  Source Video width, also rounded down to even.

Freshly generated `TrickplayInfo.Width` and the dictionary key therefore contain
the **actual generated width**, which can differ from the configured target.
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

The generation target array and the generated dictionary are intentionally not
kept in lockstep:

- When library Trickplay extraction is disabled and a `replace: false` refresh
  reaches an otherwise eligible video, Jellyfin deletes that video's Trickplay
  directory and database rows, then returns. Until such a refresh runs,
  previously generated metadata can still exist. With `replace: true`, the same
  branch deletes the old data but continues and regenerates from the current
  global options despite the disabled flag. [Disabled refresh][disabled-refresh]
- The scheduled generation task calls refresh with `replace: false` for library
  videos. [Scheduled refresh][scheduled-refresh]
- With `replace: false`, an already recorded actual width is reused. Removing a
  width from `WidthResolutions` does not delete its database row, and cleanup
  treats every existing row as expected. Adding a width can generate a new row.
  Changes that keep the same width and tile-directory identity, such as the
  interval or JPEG quality, do not replace existing data. A tile-grid change
  changes that directory identity and can regenerate and replace the row.
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

| Outcome | Meaning | Handling boundary |
| --- | --- | --- |
| Configuration unavailable or structurally unusable | `TrickplayOptions` or its width array cannot be read safely | Internal/configuration failure; do not substitute `320` |
| No configured target | The array is empty | A selection-policy failure, even if old generated rows remain |
| Multiple configured targets | Jellyfin configured several generation widths | Not an error by itself; the product must define which generated candidate wins |
| Selected Source Video is absent, remote, non-GUID, not a member of the logical item, or unauthorized | The request does not identify an allowed local Source Video | Preserve the API's authorization/not-found concealment policy |
| No generated metadata for the effective Source Video | `GetTrickplayResolutions` is empty | Not found/not ready; do not inspect directories |
| No candidate accepted by the selection policy | Generated metadata exists, but none is selectable under the approved configuration policy | Not found/configuration mismatch; log the configured targets and generated keys |
| Invalid selected metadata | Key/`Width` disagreement, non-positive dimensions, interval, tile geometry, or thumbnail count | Invalid Jellyfin metadata; do not calculate a frame or guess another width |
| Tile path empty, file absent, or file changes during GET | Metadata exists but the selected Source Sprite is unavailable | GET source-resolution failure; resolve through `ITrickplayManager`, never by path convention |
| Jellyfin manager, database, or filesystem operation throws | Availability could not be determined | Operational failure, not a normal 404 |

A raw equality test between a configured target and a generated dictionary key
is not universally valid: Jellyfin's even-width normalization and source-width
clamp can produce a different legitimate key. The follow-up design decision must
state whether the plugin accepts those normalized results and how it chooses
among multiple generated candidates. HEAD can stop after authorization,
selection, metadata validation, and Frame Index calculation; only GET needs to
resolve and inspect the Source Sprite.

[server-configuration]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Configuration/ServerConfiguration.cs#L281-L285
[trickplay-options]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Configuration/TrickplayOptions.cs#L37-L55
[library-options]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Model/Configuration/LibraryOptions.cs#L28-L118
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
[refresh-cleanup]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L198-L220
[regeneration-provider]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Providers/Trickplay/TrickplayProvider.cs#L96-L116
[eligibility-check]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Jellyfin.Server.Implementations/Trickplay/TrickplayManager.cs#L463-L491
