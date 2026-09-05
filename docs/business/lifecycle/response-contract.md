# The response contract

_Why an ETag means what it means and why `404` conceals:
[Cache identity and freshness](../design/cache-identity-and-freshness.md) and
[Authorization and visibility](../design/authorization-and-visibility.md). What the client
does with any of it: [the client](../participants/client.md). This chapter is the contract._

## Headers

| Operation | Response | Headers |
|---|---|---|
| Probe | `200`, no body | `X-Trickplay-Frame-Index`, `Cache-Control: private, no-cache` |
| Preview | `200`, JPEG body | `Content-Type: image/jpeg`, `Content-Disposition: inline`, `Content-Length`, `ETag`, `Cache-Control: private, no-cache`, `X-Trickplay-Frame-Index`, `X-Trickplay-Cache: HIT` or `MISS`, `Server-Timing` |
| Preview | `304`, no body | `ETag`, `Cache-Control: private, no-cache`, `X-Trickplay-Frame-Index`, `Server-Timing` |

The probe carries no ETag and honours no `If-None-Match`. The preview operation honours
`If-None-Match`, including the `*` form, and compares weakly. Its typed success and
not-modified outcomes carry the request's final Frame Index so `200` HIT, `200` MISS and
`304` all expose the same `X-Trickplay-Frame-Index`. Failure responses omit that header.

`Cache-Control: private, no-cache` is about *shared* caches, not the client: the answer
depends on who asked, so an intermediary must not hold it and must revalidate rather than
serve. What a client does in its own memory is its own policy.

`X-Trickplay-Cache` reports whether the bytes were reused or generated. It is diagnostic:
a client must not branch on it, and no promise is made about which requests hit.

`Server-Timing` reports the lookup stage always, and the cache, decode, and encode stages
when they ran, each with its duration in milliseconds. It is the only caller-visible trace
of coordination, so a slow preview can be attributed to a stage without server access.

`Content-Disposition: inline` states that the body is the image itself, not an attachment.

## Status codes

Both operations remain behind Jellyfin's ordinary endpoint policy, but their later gates
differ. GET resolves a current user, user visibility and logical-video playback authority;
HEAD does not. Only GET can fail on a missing sprite, because only it looks.

| Status | Meaning | Probe | Preview |
|---|---|---|---|
| `200` | Answered | yes, headers only | yes, JPEG body |
| `304` | The caller's ETag still matches | no | yes |
| `400` | Malformed query, or negative playback position | yes | yes |
| `401` | Unauthenticated caller | yes | yes |
| `403` | Ordinary endpoint policy refuses the request | yes | yes |
| `403` | No current user, including a server API key, or playback not permitted | no | yes |
| `404` | Item, Source Video, or Media Source membership unavailable | yes, user-independent | yes, user-scoped and concealing |
| `404` | No Trickplay Resolution Target, no exact metadata match, or no available frames | yes | yes |
| `404` | Source Sprite unavailable | no | yes |
| `405` | Method not supported; `Allow` advertises `GET, HEAD` | — | — |
| `500` | Configuration unreadable or inconsistent, invalid recorded metadata, or an operational failure during generation | yes | yes |

Two things this table does not distinguish, on purpose:

- **`404` conceals.** An Item hidden by library access and an Item that does not exist are
  the same GET response. HEAD does not evaluate user visibility. The gate order that
  keeps GET concealing is in
  [source resolution](source-resolution.md).
- **A successful probe does not predict a successful preview.** The probe stops before the
  sprite availability gate, so the `404` in the second-to-last row can follow a `200` probe
  for the same position. See [Trickplay Frame Probe](frame-probe.md).

## Anchors

`TrickplayPreviewController` carries both actions and maps outcomes to statuses and
headers; `PreviewQueryParameters` binds the preview operation's optional Media Source and
required position, while the probe action binds nullable raw strings so a malformed
identifier is refused rather than mis-bound; `PreviewOutcome` is the closed set behind the
preview columns above and `TrickplayFrameProbeOutcome` the set behind the probe column;
`PreviewTelemetry` feeds `Server-Timing`. The `405` advertisement and every header set are
pinned by the HTTP component suite.
