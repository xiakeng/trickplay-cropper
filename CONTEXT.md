# Trickplay Cropper

Trickplay Cropper is a Jellyfin server plugin that exposes authenticated, single-frame previews from Jellyfin-owned trickplay data.

## Language

**Trickplay Preview**:
A single JPEG frame selected by an authorized zero-based Frame Index and cropped from a Jellyfin-owned Source Sprite.
_Avoid_: Thumbnail, cropped image, preview image

**Frame Timeline**:
The authenticated calculation model for one logical Item and selected Media Source. It returns
the positive generated frame interval in Jellyfin ticks and frame count for a playback client;
it is not a permission token, representation validator, or coherence version.
_Avoid_: timeline cache, frame range, preview authorization token

**Source Sprite**:
A Jellyfin-owned trickplay JPEG containing multiple preview frames. Trickplay Cropper consumes Source Sprites but never generates them.
_Avoid_: Sprite sheet, source image, original preview

**Trickplay Resolution Target**:
A raw frame-width request in Jellyfin's server-global Trickplay configuration. Multiple targets may coexist.
_Avoid_: Configured Trickplay Resolution, configured width

**Selected Trickplay Resolution**:
The source-specific even width derived from the chosen Trickplay Resolution Target and required to match generated Trickplay metadata exactly.
_Avoid_: Effective resolution, normalized width

**Frame Index**:
The zero-based ordinal supplied by the playback client and accepted only when it is within the current authoritative generated frame count.
_Avoid_: Frame number, thumbnail index, frame position

**Preview Cache Entry**:
The cached representation of one Trickplay Preview for a specific media source, source version, Source Sprite, and Frame Index.
_Avoid_: Cache slot, cached file, preview file

**Cache Tree**:
The plugin-owned hierarchy of Preview Cache Entries beneath Jellyfin's temporary storage.
_Avoid_: Cache folder, temp directory
