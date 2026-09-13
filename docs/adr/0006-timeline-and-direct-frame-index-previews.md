# Timeline and direct Frame Index Previews

- Status: Accepted
- Date: 2026-09-13
- Supersedes: ADR 0004 and ADR 0005

## Decision

The playback client requests one authenticated Frame Timeline for the selected
logical Item and Media Source, then calculates zero-based Frame Index values
locally. Each Preview GET supplies that index directly. Timeline and Preview use
the same full current-user authorization boundary: logical Item visibility and
playback access, exact Media Source membership, and Source Video visibility.
Neither operation treats a prior Timeline or a JPEG cache entry as permission
evidence.

Every valid request reads the selected generated-metadata row authoritatively.
Preview validates the supplied index against the current positive thumbnail count
before Source Sprite lookup, crop arithmetic, cache access, conditional matching,
or encoding. Timeline additionally requires a positive interval. There is no
Probe route, server-side position selection, end clamping, observation cache,
version token, or compatibility period.

## Consequences

The direct request removes one network round trip per scrub and leaves playback
position arithmetic with the client. The server pays for an authoritative
metadata read on every valid Preview, including cache hits and conditional
requests, so current authorization and range validation cannot become stale.
Generated metadata and Source Sprites are not promised to be an atomic snapshot;
Jellyfin may perform an independent manager-owned lookup. The contract assumes
metadata remains stable for a playback session and intentionally adds no
coherence protocol or replacement cache.
