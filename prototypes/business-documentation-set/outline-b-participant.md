# Variant B — Participant

**Split by:** who is responsible for what. One chapter per party, written from
that party's point of view: what it owns, what it may assume about the others,
what it must never do. The spine is ownership, and every diagram is a sequence
diagram, because a chapter about a party is really about its edges.

## File tree

```text
docs/business/
├── README.md
├── client.md
├── jellyfin-server.md
├── frame-probe.md
├── preview-request.md
├── cache-tree.md
└── cleanup-task.md
```

## Chapters

### `README.md` — the parties and their boundaries
Who is in the room and what each one owns. Establishes the two hard ownership
lines the product depends on: Jellyfin owns the library, the trickplay
configuration, the generated metadata, and the Source Sprites; Trickplay Cropper
owns only derived, disposable artifacts — the Trickplay Preview, its identity,
and the Cache Tree. The client owns its cache policy.

- **Mermaid view:** one `flowchart LR` context diagram — four parties as boxes,
  ownership listed inside each, arrows labelled with what crosses the boundary.
  Nothing about time or order.

### `client.md` — what the client owes and may rely on
Written as a contract addressed to an integrator. The client decides when to
probe, when to reuse what it holds, and when to ask for a preview; it must carry
authorization; it may rely on `X-Trickplay-Frame-Index`, `ETag`,
`X-Trickplay-Cache`, `Server-Timing`, and `Cache-Control`. States that the cache
check is **client-owned policy** and that the plugin prescribes no key, expiry,
or invalidation rule. Also states the trap: a successful probe does not promise a
deliverable preview.

- **Mermaid view:** one `sequenceDiagram`, client-driven — scrub, probe, reuse
  decision, conditional preview request, `200` or `304`.

### `jellyfin-server.md` — what the server supplies and what it may change
Everything the plugin takes on trust: Trickplay Resolution Targets from
server-global configuration, generated trickplay metadata per Media Source,
Source Sprite files on disk, user-scoped Item lookup, playback authorization,
Media Source membership, temporary storage for the Cache Tree, and the scheduled
task host. Crucially, what the server is free to do at any time — add or drop a
target, regenerate trickplay data, replace a sprite — and why the plugin's rules
are shaped to survive that.

- **Mermaid view:** one `sequenceDiagram`, plugin asking the server for
  configuration, metadata, and a sprite path, with the three "server changed its
  mind" outcomes drawn as alternates.

### `frame-probe.md` — the plugin as Frame Index authority
The HEAD operation as a party: what it reads (configuration and metadata), what
it returns (`X-Trickplay-Frame-Index` and `Cache-Control: private, no-cache`, no
ETag, no conditional behavior), and what it refuses to touch — no Source Sprite
resolution, stat, open, or snapshot, no Cache Tree, no encoder. Explains the
authority it does *not* have: it can say which frame a position selects, and
cannot say that the frame is obtainable.

- **Mermaid view:** one `sequenceDiagram` of the probe, with the untouched
  parties shown as declared-but-never-messaged participants. A participant with
  no arrows is a stronger statement than a paragraph.

### `preview-request.md` — the plugin as preview provider
The GET operation as a party, end to end: the authorization gates, resolution
selection down to one exact Selected Trickplay Resolution with no fallback,
Frame Selection, generation from the Source Sprite, and the response contract
including every header and status. Owns the statement that the preview path
performs no second Source Video playback check beyond the logical-video gate and
Media Source membership.

- **Mermaid view:** one `sequenceDiagram` with the client, the plugin, the Cache
  Tree, and the encoder, covering both the miss-that-generates and the
  hit-that-buffers paths.

### `cache-tree.md` — the shared resource and its custodian
The Cache Tree written as a party with rights: its namespace and layout, what
counts as one Preview Cache Entry, the identity inputs and the source version
stamp, and the custodial rules that let many callers share it — shared tree
lease, then entry lock, work, buffer, release; write temporary then publish
atomically; losing the publication race is a hit. This chapter is where the
concurrency rules live in variant B, and it is the variant's weak point: a
resource is not really an actor, so the sequence diagrams here have to invent a
speaker.

- **Mermaid view:** one `sequenceDiagram` with two concurrent callers and the
  Cache Tree, showing acquisition order and the race.

### `cleanup-task.md` — the maintenance party
The scheduled run as a participant that must be polite: the cutoff that keeps an
in-flight entry untouchable, classification of a discovered file into entry,
orphan temporary, or skip, the re-check that the file is unchanged before
deletion, exclusive tree leases for orphan and directory work, and pruning empty
directories last. Also what it never does: it does not repair, regenerate, or
reclaim anything Jellyfin owns.

- **Mermaid view:** one `sequenceDiagram` between the cleanup run, the Cache
  Tree, and an in-flight request, showing the cutoff making the live entry
  invisible to the run.

## What this variant is good at

- An integrator reads `client.md` and stops; a server-side debugger reads
  `jellyfin-server.md` and stops. Each party's contract is in one place.
- Ownership disputes are settled explicitly: who may mutate what is stated per
  party, which is exactly the confusion ADR 0002 exists to prevent.
- Sequence diagrams are the right tool for every chapter, so the set is visually
  coherent.

## What it is weak at

- **No chapter tells you what happens, in order, for one request.** A newcomer
  has to read four chapters and assemble the pipeline themselves.
- Internal mechanism has no natural home. `cache-tree.md` has to speak for a
  resource, and Frame Selection ends up split between `frame-probe.md` and
  `preview-request.md`, which invites divergence between the two statements of
  the same arithmetic.
- Cross-party rules must be stated twice (once per party) or linked, and the
  duplicated statement is the one that rots.
