# Separate Frame Probe calculation from Preview authorization

GET and HEAD answer different questions and must not share an authorization context.
GET returns or revalidates a representation, so it resolves a current Jellyfin user,
conceals user-invisible Items, checks logical-video playback authority, and proves source
membership and Source Video visibility through user-scoped host APIs before any `200` or
`304`. HEAD answers only which generated Frame Index the requested position selects after
Jellyfin's ordinary endpoint policy accepts the request. It therefore resolves exact Item
and Source Video identities without a user, proves membership through the full playback
Media Source enumeration with explicit media probing disabled, and makes no visibility or
playback decision. A userless API key may consequently receive a successful HEAD while
the corresponding GET remains forbidden. The two paths share one request-local target,
metadata, and Frame Index calculation whose inputs contain no identity or authorization
state; neither path retains configuration or metadata across requests. Every successful
GET `200` or `304` carries that final Frame Index in `X-Trickplay-Frame-Index`, independent
of the representation ETag. This split keeps HEAD structurally unable to become permission
evidence while preserving one deterministic calculation for both operations.
