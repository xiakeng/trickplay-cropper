# Cache ordered generated-metadata observations with bounded freshness

Trickplay Cropper retains immutable generated-metadata observations by effective Source
Video and exact Selected Trickplay Resolution. Positive metadata is current for 30 minutes;
whole-source and selected-width absence is current for 5 minutes. Both are absolute ages
measured from the authoritative read start. HEAD may reuse either after current source and
target checks. GET keeps its user-scoped authorization and source checks first, short-circuits
only an applicable current negative, and otherwise performs an authoritative metadata read
even for a JPEG cache hit or conditional request.

Every issued read receives a read-start order. Publication is coherent and refuses an older
observation that would overwrite a newer positive or negative, or resurrect a value removed
by newer invalid metadata. Caller cancellation cannot cancel Jellyfin's issued metadata
query; the plugin therefore observes it through settlement and applies the same publication
rules. Completed ordering state and expired observations are reclaimed when no older read is
still in progress. Request traffic drives a cross-source reclamation pass at most once per
negative lifetime, so idle-source state expires without an internal timer. The cache does not
retain configuration, source inputs, request position,
authorization, Source Sprite facts, operational failures, cancellation, or invalid metadata,
and it adds no capacity limit, queue, coalescing, event invalidation, or filesystem polling.
