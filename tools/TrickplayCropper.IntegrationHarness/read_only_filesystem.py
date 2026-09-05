"""Deny filesystem writes in the single-threaded SQLite query child via Landlock.

No privileges are gained: PR_SET_NO_NEW_PRIVS is set before restricting self.
The ABI and syscall numbers are Linux x86-64/aarch64; other hosts fail closed.
See https://docs.kernel.org/userspace-api/landlock.html.
"""
import ctypes
import os

CREATE_RULESET = 444
RESTRICT_SELF = 446
PR_SET_NO_NEW_PRIVS = 38


def restrict_writes():
    if os.uname().machine not in ("x86_64", "aarch64"):
        raise OSError("Unsupported local query architecture")
    libc = ctypes.CDLL(None, use_errno=True)
    libc.syscall.restype = ctypes.c_long
    abi = libc.syscall(CREATE_RULESET, 0, 0, 1)
    if abi < 3:
        raise OSError("Landlock ABI 3 is required for write and truncate restrictions")
    # WRITE_FILE, REMOVE_DIR/FILE, MAKE_*, REFER, and TRUNCATE; reads stay allowed.
    rights = ctypes.c_uint64((1 << 1) | sum(1 << bit for bit in range(4, 15)))
    descriptor = libc.syscall(CREATE_RULESET, ctypes.byref(rights), ctypes.sizeof(rights), 0)
    if descriptor < 0:
        raise OSError("Cannot establish a read-only query process")
    try:
        if libc.prctl(PR_SET_NO_NEW_PRIVS, 1, 0, 0, 0) != 0:
            raise OSError("Cannot restrict query privileges")
        if libc.syscall(RESTRICT_SELF, descriptor, 0) != 0:
            raise OSError("Cannot restrict query filesystem access")
    finally:
        os.close(descriptor)
