"""Prove only the supplied Item's existence without user visibility or database writes.

SQLite opens the source in read-only mode with query_only enabled, including
the live WAL. Existing host-owned shared memory must be readable in WAL mode.
No titles, paths, other Items, or media contents are read.
"""
import pathlib
import sqlite3
import sys
import uuid

DATABASE = pathlib.Path("/var/lib/jellyfin/data/jellyfin.db")


def exists(item_id):
    item = uuid.UUID(item_id)
    with sqlite3.connect(DATABASE.as_uri() + "?mode=ro", uri=True) as database:
        database.execute("PRAGMA query_only = ON")
        found = database.execute(
            "SELECT 1 FROM BaseItems WHERE Id = ? COLLATE NOCASE LIMIT 1", (str(item),)).fetchone() is not None
    return found


if __name__ == "__main__":
    try:
        sys.exit(0 if len(sys.argv) == 2 and exists(sys.argv[1]) else 1)
    except Exception:
        sys.exit(2)
