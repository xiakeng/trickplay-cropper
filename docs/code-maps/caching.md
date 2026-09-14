# Caching map

Preview caching has one derived boundary. Each Preview GET reads current authorized
metadata and Source Sprite facts, then the disk cache owns generated JPEG entries.
Preview Cache Coordination orders Cache Tree and entry leases; the scheduled task
empties the tree.

| Area | Map | Relationship |
|---|---|---|
| Preview Cache Entries | [Disk Preview cache](caching/disk-preview.md) | GET reads or publishes one JPEG under coordination; cleanup owns candidates before deletion |
| Metadata and source facts | [Request paths](request-paths.md) | GET and Frame Timeline perform current authorized reads; only GET resolves a sprite and reaches disk and encoding |
