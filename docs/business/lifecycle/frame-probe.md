# Retired Frame Probe

The former HEAD Trickplay Frame Probe is retired. Current clients request one authenticated
Frame Timeline, calculate direct zero-based Frame Index values locally, and call the Preview GET
endpoint. The server no longer accepts playback positions, performs position-to-index
calculation or end clamping, or exposes a Probe route.

The historical probe design remains in repository history and superseded ADRs for provenance;
it is not part of the current HTTP or business contract.
