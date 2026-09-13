# Caching map

Preview caching has one boundary: the disk cache owns derived Preview Cache Entries.
Preview Cache Coordination orders Cache Tree and entry leases, and the scheduled task
empties the tree. Authoritative metadata reads are request-scoped and are not cached.

| Area | Map | Relationship |
|---|---|---|
| Preview Cache Entries | [Disk Preview cache](caching/disk-preview.md) | Preview GET reads or publishes one JPEG under coordination; cleanup owns candidates before deletion |
| HTTP consumers | [Request paths](request-paths.md) | Preview GET reaches disk and encoding; Frame Timeline stops after authorization and metadata calculation |
