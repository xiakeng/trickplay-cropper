# The response contract

_Why authorization and validation precede representation identity:
[Authorization and visibility](../design/authorization-and-visibility.md) and
[Cache identity and freshness](../design/cache-identity-and-freshness.md). This chapter is
the contract._

## Frame Timeline

`GET /TrickplayCropper/Videos/{itemId}/FrameTimeline` returns `200 application/json` with
exactly two lower-camel-case fields:

```json
{"intervalTicks": 100000000, "frameCount": 77}
```

Both values are positive; `intervalTicks` is a checked 64-bit Jellyfin-tick conversion. The
response has `Cache-Control: private, no-cache` and no ETag or Last-Modified. Conditional
headers do not produce `304`. The endpoint performs no Source Sprite, Preview Cache, or encoder
work.

## Preview

Successful `GET /TrickplayCropper/Videos/{itemId}/Preview` responses preserve:

| Response | Headers |
|---|---|
| `200` | `Content-Type: image/jpeg`, `Content-Disposition: inline`, `Content-Length`, opaque strong `ETag`, `Cache-Control: private, no-cache`, `X-Trickplay-Cache: HIT` or `MISS`, `Server-Timing` |
| `304` | `ETag`, `Cache-Control: private, no-cache`, `Server-Timing` |

`If-None-Match` exact, weak, and wildcard matches return a bodyless `304`, but only after
current authorization, authoritative metadata resolution, index validation, and identity
creation. `X-Trickplay-Frame-Index` is not emitted.

## Status codes

| Status | Meaning |
|---|---|
| `400` | Missing, malformed, negative, or authoritative-count-out-of-range `FrameIndex` |
| `401` | Authentication failure or no usable current user |
| `403` | Authenticated denial, including a userless API key |
| `404` | Concealed or unavailable Item, source, exact metadata, or (Preview only) Source Sprite |
| `500` | Invalid configuration/server data, I/O, encoding, or unexpected processing failure |

Error bodies are not a plugin-specific DTO contract. Both operations reauthorize every request;
a prior Timeline, JPEG, or ETag never supplies permission or range evidence.

## Anchors

`TrickplayPreviewController` maps `FrameTimelineOutcome` and `PreviewOutcome` to the wire
contract. `PreviewQueryParameters` binds the required 32-bit `FrameIndex` and optional
`MediaSourceId`; `PreviewTelemetry` feeds `Server-Timing`. `TrickplayFrameTimeline` owns the
two-field JSON result, while `TrickplayPreview` owns JPEG and conditional outcomes.
