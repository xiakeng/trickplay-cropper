"""Two bounded privileged phases. No credentials, discovery, or evidence files."""
import json
import os
import pathlib
import pwd
import re
import shutil
import signal
import stat
import subprocess
import sys
import uuid

PLUGIN_ID = uuid.UUID("630fb758-9a29-4f2c-a54c-95793651bb8a")
ASSEMBLY = "Jellyfin.Plugin.TrickplayCropper"
CATEGORY = ASSEMBLY
REFUSED = 20
BEFORE_SNAPSHOT = 21
AFTER_SNAPSHOT = 22
RESTORE_FAILED = 23


def contained(path):
    """Refuse links in a mutation path, including links in existing parents."""
    if path.absolute() != path.resolve():
        raise ValueError("Mutation path contains a symbolic link")


class HostOperation:
    """Own the snapshot and exact allowed filesystem mutations of a local cycle."""

    def __init__(self, logging, plugins, cache, restart):
        self.logging = logging
        self.snapshot = logging.with_name(logging.name + ".bak")
        self.plugins = plugins
        self.cache = cache
        self.restart = restart

    def prepare(self, binary, version):
        if os.path.lexists(self.snapshot):
            return REFUSED
        owned = False
        try:
            if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version):
                raise ValueError("Invalid deployment version")
            for suffix in ("dll", "pdb"):
                if not (binary / (ASSEMBLY + "." + suffix)).is_file():
                    raise ValueError("Missing Debug artifact")
            contained(self.logging)
            info = self.logging.stat()
            original = self.logging.read_bytes()
            edited = self.override(original)
            # The snapshot itself is the sole concurrency guard. Claim it before any
            # destructive work; a check followed by a later copy permits two owners.
            with self.snapshot.open("xb") as snapshot:
                snapshot.write(original)
                snapshot.flush()
                os.fsync(snapshot.fileno())
                owned = True
            shutil.copystat(self.logging, self.snapshot)
            os.utime(self.snapshot, ns=(info.st_atime_ns, info.st_mtime_ns))
            os.chown(self.snapshot, info.st_uid, info.st_gid)
            self.deploy(binary, version)
            self.logging.write_bytes(edited)
            result = 0
        except FileExistsError:
            result = AFTER_SNAPSHOT if owned else REFUSED
        except Exception:
            # Do not emit raw exceptions: paths and host configuration are private.
            result = AFTER_SNAPSHOT if owned else BEFORE_SNAPSHOT
        finally:
            if owned:
                try:
                    self.restart()
                except Exception:
                    result = AFTER_SNAPSHOT
        return result

    def deploy(self, binary, version):
        contained(self.plugins)
        contained(self.cache)
        destination = self.plugins / ("Trickplay Cropper_" + version)
        matches = []
        for folder in self.plugins.iterdir() if self.plugins.exists() else []:
            if not folder.is_dir() or folder.is_symlink():
                continue
            metadata = folder / "meta.json"
            if metadata.is_file() and not metadata.is_symlink():
                try:
                    if uuid.UUID(json.loads(metadata.read_bytes())["guid"]) == PLUGIN_ID:
                        matches.append(folder)
                except (ValueError, KeyError, TypeError):
                    continue
        if os.path.lexists(destination) and destination not in matches:
            raise ValueError("Deployment destination is not a matching installation")
        for folder in matches:
            shutil.rmtree(folder)
        if self.cache.exists():
            for entry in self.cache.iterdir():
                if entry.is_dir() and not entry.is_symlink():
                    shutil.rmtree(entry)
                else:
                    entry.unlink()
        destination.mkdir(mode=0o755, parents=True)
        account = pwd.getpwnam("jellyfin") if os.geteuid() == 0 else pwd.getpwuid(os.geteuid())
        os.chmod(destination, 0o755)
        os.chown(destination, account.pw_uid, account.pw_gid)
        for suffix in ("dll", "pdb"):
            target = destination / (ASSEMBLY + "." + suffix)
            shutil.copyfile(binary / target.name, target)
            os.chmod(target, 0o644)
            os.chown(target, account.pw_uid, account.pw_gid)

    def restore(self):
        try:
            contained(self.snapshot)
            contained(self.logging)
            info = self.snapshot.stat()
            shutil.copy2(self.snapshot, self.logging)
            os.chown(self.logging, info.st_uid, info.st_gid)
            if self.logging.read_bytes() != self.snapshot.read_bytes():
                return RESTORE_FAILED
            os.utime(self.logging, ns=(info.st_atime_ns, info.st_mtime_ns))
            self.snapshot.unlink()
            self.restart()
            return 0
        except Exception:
            return RESTORE_FAILED

    @staticmethod
    def override(original):
        configuration = json.loads(original)
        serilog = configuration["Serilog"]
        minimum = serilog.get("MinimumLevel", "Information")
        if isinstance(minimum, str):
            minimum = {"Default": minimum}
            serilog["MinimumLevel"] = minimum
        minimum.setdefault("Override", {})[CATEGORY] = "Debug"
        return (json.dumps(configuration, indent=2) + "\n").encode()


def restart():
    subprocess.run(["/usr/bin/systemctl", "restart", "jellyfin.service"],
                   check=True, timeout=90, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def main(arguments):
    if os.geteuid() != 0:
        return BEFORE_SNAPSHOT
    # Ctrl+C cancels HTTP work in the driver, not a partly completed privileged phase.
    signal.signal(signal.SIGINT, signal.SIG_IGN)
    operation = HostOperation(pathlib.Path("/etc/jellyfin/logging.json"),
                              pathlib.Path("/var/lib/jellyfin/plugins"),
                              pathlib.Path("/var/lib/jellyfin/temp/Jellyfin.Plugin.TrickplayCropper/preview-v1"), restart)
    if arguments == ["restore"]:
        return operation.restore()
    if len(arguments) == 3 and arguments[0] == "prepare":
        return operation.prepare(pathlib.Path(arguments[1]), arguments[2])
    return BEFORE_SNAPSHOT


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
