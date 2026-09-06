# Final request contract verification (#95)

## Controlled live comparison, 2026-09-06

The comparison used the same native Jellyfin **10.11.11** at localhost:8096, Debug
plugin builds, plugin Debug logging override, administrator user token, two supplied
media Items, HTTP/1.1 connection reuse per lane, and fixed request distribution.
`harness.json` was used in place and not rewritten. No media, generation configuration,
user policy, or credentials were changed. Each side began after deployment, process
restart, and plugin Cache Tree clearing; OS filesystem caches were not flushed.

- Before: `6aaaf27` (#92, the final authorization split before observation caches).
  Deployed DLL SHA-256: `0930980a2b4df5acd2c4e2cbd0b66df8b3073881a46e6889a7dd5a3aa0141100`.
- After: `ddeaf33` (#94, both observation caches; #95 does not modify production code).
  Deployed DLL SHA-256: `3557d6475b833a3bbbc8d41648a05aa95fb26e733a32153ed4121fa6b0cc218c`.
- Both fixtures: generated width 320, height 180, interval 10,000 ms;
  generated counts 732 and 508. Each supplied Item enumerated only its default source.
- Each side: **750 measured Preview requests**, including 120 serial requests per
  method and six lanes of 60 alternating HEAD/GET requests. Independent host metadata
  and readiness queries are excluded from this count. Every measured status matched.
- Elapsed measured workload: **5.214 s before / 4.909 s after**. Logging restoration
  was byte-identical and host health passed after each cycle.

| Category | Samples per side | Before median (ms) | After median (ms) |
| --- | ---: | ---: | ---: |
| First source/metadata HEAD load | 2 | 15.471 | 19.146 |
| First JPEG GET MISS | 2 | 54.109 | 53.015 |
| Explicit default-source HEAD | 2 | 11.834 | 11.502 |
| Serial warm HEAD | 120 | 8.643 | 7.302 |
| Serial GET JPEG HIT | 120 | 12.518 | 12.252 |
| New-frame GET JPEG MISS | 8 | 14.074 | 13.735 |
| Conditional GET 304 | 8 | 11.937 | 11.757 |
| Repeated nonmember-source HEAD 404 | 58 | 7.969 | 6.969 |
| Repeated nonmember-source GET 404 | 58 | 11.300 | 11.513 |
| Six-lane warm HEAD | 180 | 12.789 | 11.653 |
| Six-lane GET JPEG HIT | 180 | 18.619 | 18.891 |

Serial warm HEAD's observed median fell **15.5%**, and six-lane HEAD fell **8.9%**.
GET differences are small, with six-lane GET slightly slower. Two first-load samples
are diagnostic observations, not a reliable cold-start distribution. This is one
matched pair, without randomized run order or statistical confidence claims; older
unmatched medians are not used as the baseline. No numeric latency budget is asserted.

Ignored Markdown and JSON retain every sample, count, minimum, maximum, median, mean,
request volume, elapsed time, deployed binary digest, generated dimensions/counts, and
GET Server-Timing. No token, user ID, Item ID, media path, or title appears in the reports.
`request_costs.py` is the reproducible measurement driver; deployment uses the existing
`host_operation.py` prepare/restore phases with unconditional restoration. A preliminary
attempt encountered startup unavailability before any measured request; its failed,
zero-sample report is retained separately. The driver now waits for host readiness.

## Evidence boundaries

| Contract | Deterministic automated evidence | Supported live host evidence |
| --- | --- | --- |
| Ordinary authentication and HEAD/GET policy split | TestServer and Kestrel with fake authentication; API-key HEAD 200/GET 403, disabled/revoked session and global-policy refusal | Supplied user token accepted; invented token HEAD/GET 401. Userless API key not supplied; revocation/global-policy mutation not run. Optional `userlessApiKey` now supports a real supplied fixture. |
| GET authorization despite reusable observations | `AllowsInvisibleGeneratedMediaToBeProbedButNotFetched`, playback denial before negative lookup, cache-hit authorization | Supplied concealed Item GET 404 only. Its generated data is unverified, so no live authorization-proof claim. |
| Source compatibility | Full-enumeration doubles exercise default, alternate, linked and eligible dynamic sources; current GET membership and width checks | Default and explicit default IDs passed; only one source per supplied Item. Alternate/linked/dynamic live fixtures unavailable. |
| Generated interval and exact width | Deliberately different configured/generated intervals, odd targets, clamp and exact-key failures | Both supplied default sources: 320x180 JPEG, generated 10 s interval, correct start/beyond-end and nonzero Frame Index. No alternate interval/width live mutation. |
| Positive lifetime/renewal | `ReusesPositiveGeneratedMetadataForThirtyMinutesWithoutSliding`, `GetRefreshesPositiveMetadataForImageHitsAndConditionalRequests` | GET 200/HIT/304 and subsequent HEAD passed; no 30-minute live mutation experiment. |
| Negative lifetime and GET short circuit | `ReusesWholeSourceAbsenceForFiveMinutesAfterCurrentGetAuthorization`, selected-width and no-thumbnail negatives at exactly five minutes | Repeated nonmember-source HEAD/GET 404 measured. GET source negatives are not metadata-negative hits. No authorized visible metadata-absence fixture supplied. |
| Source freshness | Independent source/metadata ages, GET width replacement, membership recheck, relinking/deletion and expired in-flight observations | Static supplied source inputs only; no operator media mutation. |
| Deletion/failed regeneration and concurrency | `FailedRegenerationAfterDeletionNeverFallsBackToOldPositiveMetadata`, invalid observations, ordered positive/absence races, canceled reads and reclamation | Six-lane traffic and Scrub Storm; no destructive generation/deletion fixture. |
| HEAD/GET parity | GET's actual 200/304 header; older HEAD snapshot followed by refreshed GET; harness stale-HEAD regression | Boundary checks refresh through GET, compare independent equal generated snapshots, then HEAD. No cross-refresh equality requirement. |

All expiry, regeneration, provider-enumeration and backing-operation observations above
run in the existing HTTP/component seam with host doubles and fake time. A Kestrel test
proves real HTTP framing, not Jellyfin authentication or provider behavior. The live
host uses its actual authentication, plugins, metadata, Source Sprites, and JPEG cache.
Unavailable live fixtures remain unperformed; they are not silently manufactured.

## Remaining costs and regression checks

`WarmProbePerformsNoPluginSourceOrMetadataIo` observes two library lookups, one source
enumeration and one metadata query on cold HEAD; warm HEAD adds **zero** of each, zero
plugin user lookups, zero sprite-path requests and zero cache operations. The negative
metadata test similarly proves no second metadata read, sprite path or cache access
before expiry, while GET playback refusal still wins. These are backing API operation
observations, not new production telemetry headers or live filesystem tracing.

The default Jellyfin authorization policy can still query users, so warm HEAD's live
latency is not zero even when plugin backing calls disappear. GET retains current user,
visibility and source checks, always refreshes positive metadata, and its full host
tile-path API may independently read metadata. Existing Server-Timing covers plugin
lookup/cache/encode stages, not the whole host authentication and middleware pipeline;
client latency does not isolate those host costs. Their exact attribution is unmeasured.

Harness regressions include malformed/missing 304 Frame Index, wrong status/body/ETag,
value-equal independently parsed ETags, optional userless API-key errors, stale valid
HEAD observations, and redacted partial performance reports. The real-host 304 check
caught a reference-equality assertion in the new harness; an independently constructed
ETag in the test double reproduced it, and the check now compares values.

## Repeated live functional run

Two consecutive final live runs passed all four smoke cases, each including **864 HEAD
and 864 GET** Scrub Storm requests. Each reconciled 864 FrameSelected events, 97 MISS,
767 HIT, and 101 distinct Preview identities against the actual log and Cache Tree,
with identical repeated JPEG bytes/ETags and no temporary residue. Both restored
logging byte-for-byte and passed final host health.

| Final Scrub Storm run | HTTP elapsed (s) | HEAD median (ms) | GET MISS median (ms) | GET HIT median (ms) |
| --- | ---: | ---: | ---: | ---: |
| 1 | 5.753 | 12.441 | 27.561 | 21.270 |
| 2 | 5.686 | 12.665 | 27.207 | 21.976 |

These functional stress runs have a different distribution from the controlled pair
and are reported separately. During iteration an immediate post-restoration preflight
failed in the startup window. Subject validation now uses the existing bounded API
readiness gate before checking users/Items; regression coverage retries startup `503`
and does not retry `401`. The final consecutive runs passed with that correction.

Release verification passed with zero warnings/errors: locked restore, formatter
verification, **240 unit tests and 331 component tests**, including the isolated Python
harness contracts. Python report checks also verify retained status groups and elapsed
time on transport failure without copying private exception text.
