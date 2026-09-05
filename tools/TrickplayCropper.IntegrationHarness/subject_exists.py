"""Prove only the supplied Item's existence without user visibility or database writes.

SQLite opens the source in read-only mode with query_only enabled, including
the live WAL. Existing host-owned shared memory must be readable in WAL mode.
No titles, paths, other Items, or media contents are read.
"""
import pathlib
import sqlite3
import sys
import uuid

from read_only_filesystem import restrict_writes

DATABASE = pathlib.Path("/var/lib/jellyfin/data/jellyfin.db")


def signature(path):
    try:
        info = path.stat()
        return info.st_ino, info.st_size, info.st_mtime_ns
    except FileNotFoundError:
        return None


def exists(item_id):
    item = uuid.UUID(item_id)
    wal = DATABASE.with_name(DATABASE.name + "-wal")
    before = signature(DATABASE), signature(wal)
    # SQLite normally tries to create a missing WAL even for mode=ro. A stable
    # checkpoint with no WAL can instead be read immutably without creating files.
    mode = "?mode=ro" if before[1] is not None else "?mode=ro&immutable=1"
    with sqlite3.connect(DATABASE.as_uri() + mode, uri=True) as database:
        database.execute("PRAGMA query_only = ON")
        found = database.execute(
            "SELECT 1 FROM BaseItems WHERE Id = ? COLLATE NOCASE LIMIT 1", (str(item),)).fetchone() is not None
    if before != (signature(DATABASE), signature(wal)):
        raise ValueError("The database changed during subject validation; retry the manual check")
    return found


if __name__ == "__main__":
    try:
        restrict_writes()
        sys.exit(0 if len(sys.argv) == 2 and exists(sys.argv[1]) else 1)
    except Exception:
        sys.exit(2)
