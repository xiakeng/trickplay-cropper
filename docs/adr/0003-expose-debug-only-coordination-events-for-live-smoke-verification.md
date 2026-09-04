# Expose Debug-only coordination events for live smoke verification

The manual Scrub Storm cannot distinguish cache, entry-lock, Cache Tree lease, and decode-permit coordination from HTTP and filesystem results alone, while parsing free-form logs would make the harness brittle. Trickplay Cropper therefore exposes stable EventId/EventName structured events at Debug level for those waits and outcomes, plus Frame Index and sprite index, with redaction-safe fields and no effect on request behavior; contention observations remain diagnostics rather than live pass gates.
