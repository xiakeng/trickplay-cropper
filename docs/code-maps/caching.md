# Caching map

Preview caching has three boundaries. Two observation caches retain the last
authoritative host read; the disk cache owns derived Preview Cache Entries. Preview
Cache Coordination orders Cache Tree and entry leases. The scheduled task empties the
tree.

| Area | Map | Relationship |
|---|---|---|
| Preview Cache Entries | [Disk Preview cache](caching/disk-preview.md) | GET reads or publishes one JPEG under coordination; cleanup owns candidates before deletion |
| Source and metadata facts | [Observation caches](caching/observations.md) | GET publishes verified facts; a warm Frame Probe reuses eligible observations |
| HTTP consumers | [Request paths](request-paths.md) | GET and the Frame Probe converge on calculation, then only GET reaches disk and encoding |
