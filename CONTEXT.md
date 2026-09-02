# Trickplay Cropper

Trickplay Cropper is a Jellyfin server plugin that exposes authenticated, single-frame previews from Jellyfin-owned trickplay data.

## Language

**Trickplay Preview**:
A single JPEG frame selected for an authorized playback position and cropped from a Jellyfin-owned Source Sprite.
_Avoid_: Thumbnail, cropped image, preview image

**Source Sprite**:
A Jellyfin-owned trickplay JPEG containing multiple preview frames. Trickplay Cropper consumes Source Sprites but never generates them.
_Avoid_: Sprite sheet, source image, original preview

**Trickplay Resolution Target**:
A raw frame-width request in Jellyfin's server-global Trickplay configuration. Multiple targets may coexist.
_Avoid_: Configured Trickplay Resolution, configured width

**Selected Trickplay Resolution**:
The source-specific even width derived from the chosen Trickplay Resolution Target and required to match generated Trickplay metadata exactly.
_Avoid_: Effective resolution, normalized width

**Preview Cache Entry**:
The cached representation of one Trickplay Preview for a specific media source, source version, sprite, and frame.
_Avoid_: Cache slot, cached file, preview file

**Cache Tree**:
The plugin-owned hierarchy of Preview Cache Entries beneath Jellyfin's temporary storage.
_Avoid_: Cache folder, temp directory
