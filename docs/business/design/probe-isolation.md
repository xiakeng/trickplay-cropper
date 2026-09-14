# Retired Frame Probe isolation

The former HEAD Frame Probe is retired. The current low-cost calculation operation is the
authenticated Frame Timeline: it reads the current user, selected source, and generated
metadata, then returns `intervalTicks` and `frameCount` without Source Sprite, Preview Cache,
conditional, or encoder work.

The Timeline is not permission evidence for later Preview requests and does not promise that a
Source Sprite exists. Historical probe rationale remains in superseded ADRs; this file is kept
only to prevent the retired route from being mistaken for current behavior.
