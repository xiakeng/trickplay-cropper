# Jellyfin 10.11.11 Frame Probe Source Enumeration Contract

## Scope and sources

This note records the supported Jellyfin behavior used by the Trickplay Frame Probe's
user-independent source adapter. The repository pins Jellyfin Server 10.11.11. Source
links are fixed to commit `1fbd8739292cce610231be93daf43368733edf63`, the source commit
for the official `v10.11.11` tag. [Jellyfin release tag][release]
[Jellyfin source commit][commit]

## Supported host call

The adapter calls:

```csharp
GetPlaybackMediaSources(
    logicalVideo,
    user: null,
    allowMediaProbe: false,
    enablePathSubstitution: false,
    cancellationToken)
```

This is the full host enumeration, not a static-only shortcut. Jellyfin first obtains the
Item's static Media Sources, skips only the conditional metadata refresh guarded by
`allowMediaProbe`, always asks every dynamic Media Source provider, combines the results,
and sorts them. User-specific stream defaults and transcoding/remux permissions are shaped
only when `user` is non-null. [Playback enumeration][playback-enumeration]
[Dynamic providers][dynamic-providers] [Static sources][static-sources]

`allowMediaProbe: false` therefore means **no explicit refresh/probe triggered by a
missing primary stream or `.strm` path**. It does not mean static-only, and it does not
promise that dynamic providers perform no I/O. Provider failures are caught by Jellyfin
and omit that provider's sources. The plugin does not add retries or a second provider
path.

For a `Video`, the static source graph starts with the Item itself, includes linked
alternate versions, includes the primary grouping when applicable, and includes local
alternate version Items. `BaseItem.GetMediaSources` turns each into a `MediaSourceInfo`,
resolves filesystem link targets through Jellyfin's filesystem abstraction, assigns the
Item GUID as the Media Source ID, and carries its media streams. [Video source graph]
[Media Source construction][source-construction]

## Plugin boundary

The probe requires the unscoped logical lookup to return the requested `Video` identity,
the requested Media Source GUID to appear in the full enumeration, and the unscoped Source
Video lookup to return that exact identity. The matched `MediaSourceInfo.VideoStream.Width`
is the sole normalization source width. Default, local alternate, linked and eligible
dynamic source shapes all pass through this same membership rule; there is no plugin
fallback or path reconstruction.

The user-independent lookups and membership test establish real host identities, not user
visibility or playback authority. A successful probe is calculation availability only.
GET keeps its separate current-user, visibility, playback, user-shaped membership and
Source Video visibility gates.

## Automated seam and its limits

The HTTP component suite supplies an `IMediaSourceManager` fake that validates the exact
HEAD call arguments and returns representative default, local alternate, linked and
eligible dynamic `MediaSourceInfo` shapes. It proves plugin selection, identity checks,
normalization width use, and the absence of current-user and user-scoped calls. It does
**not** instantiate Jellyfin's `MediaSourceManager`, alternate-version graph, filesystem
link resolution, or dynamic providers. Those host behaviors are supported by the pinned
source above, not claimed as live integration evidence.

The manual Integration Harness must not turn an invisible Item into a mandatory positive
HEAD case unless the operator fixture independently establishes generated metadata and
full-enumeration membership. It continues to verify GET concealment and verifies positive
HEAD calculation only for known playable fixtures. Broader provider and filesystem
behavior remains outside the harness unless a future fixture explicitly supplies it.

[release]: https://github.com/jellyfin/jellyfin/releases/tag/v10.11.11
[commit]: https://github.com/jellyfin/jellyfin/commit/1fbd8739292cce610231be93daf43368733edf63
[playback-enumeration]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Emby.Server.Implementations/Library/MediaSourceManager.cs#L170-L224
[dynamic-providers]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Emby.Server.Implementations/Library/MediaSourceManager.cs#L290-L334
[static-sources]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/Emby.Server.Implementations/Library/MediaSourceManager.cs#L348-L375
[video-source-graph]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Entities/Video.cs#L533-L564
[source-construction]: https://github.com/jellyfin/jellyfin/blob/1fbd8739292cce610231be93daf43368733edf63/MediaBrowser.Controller/Entities/BaseItem.cs#L1083-L1217
