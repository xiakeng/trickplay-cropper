"""Real-filesystem checks; service restarts are recorded, never performed."""
import importlib.util
import pathlib
import tempfile
import unittest

HARNESS_DIRECTORY = pathlib.Path(__file__).resolve().parents[2] / "tools/TrickplayCropper.IntegrationHarness"
spec = importlib.util.spec_from_file_location("host_operation", HARNESS_DIRECTORY / "host_operation.py")
host = importlib.util.module_from_spec(spec)
spec.loader.exec_module(host)


class HostOperationSpecs(unittest.TestCase):
    def test_restores_exact_bytes_and_retains_deployment(self):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            logging = folder / "logging.json"
            original = b'{"Serilog":{"MinimumLevel":"Information","WriteTo":[]}}\r\n'
            logging.write_bytes(original)
            binary = folder / "build"
            binary.mkdir()
            for suffix in ("dll", "pdb"):
                (binary / (host.ASSEMBLY + "." + suffix)).write_bytes(suffix.encode())
            restarts = []
            operation = host.HostOperation(logging, folder / "plugins", folder / "cache", lambda: restarts.append(1))
            self.assertEqual(0, operation.prepare(binary, "1.0.0.0"))
            self.assertNotEqual(original, logging.read_bytes())
            self.assertEqual(0, operation.restore())
            self.assertEqual(original, logging.read_bytes())
            self.assertFalse(operation.snapshot.exists())
            self.assertTrue((folder / "plugins" / "Trickplay Cropper_1.0.0.0" / (host.ASSEMBLY + ".dll")).exists())
            self.assertEqual([1, 1], restarts)


    def test_surviving_snapshot_blocks_all_mutations(self):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            logging = folder / "logging.json"
            logging.write_bytes(b"unchanged")
            snapshot = folder / "logging.json.bak"
            snapshot.write_bytes(b"previous run")
            operation = host.HostOperation(logging, folder / "plugins", folder / "cache", lambda: self.fail("restart"))
            self.assertEqual(host.REFUSED, operation.prepare(folder, "1.0.0.0"))
            self.assertEqual(b"unchanged", logging.read_bytes())
            self.assertEqual(b"previous run", snapshot.read_bytes())
            self.assertFalse((folder / "plugins").exists())

    def test_failed_first_restart_still_restores_the_original(self):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            logging = folder / "logging.json"
            original = b'{"Serilog":{"MinimumLevel":{"Default":"Warning","Override":{"Other":"Error"}},"WriteTo":[]}}'
            logging.write_bytes(original)
            logging.chmod(0o640)
            binary = folder / "build"
            binary.mkdir()
            for suffix in ("dll", "pdb"):
                (binary / (host.ASSEMBLY + "." + suffix)).write_bytes(suffix.encode())
            restarts = []
            def restart():
                restarts.append(1)
                if len(restarts) == 1:
                    raise OSError("simulated systemd failure")
            operation = host.HostOperation(logging, folder / "plugins", folder / "cache", restart)
            self.assertEqual(host.AFTER_SNAPSHOT, operation.prepare(binary, "1.0.0.0"))
            self.assertEqual(0o640, operation.snapshot.stat().st_mode & 0o777)
            self.assertEqual(0, operation.restore())
            self.assertEqual(original, logging.read_bytes())
            self.assertEqual(0o640, logging.stat().st_mode & 0o777)
            self.assertEqual([1, 1], restarts)

    def test_override_preserves_default_other_categories_and_sinks(self):
        import json
        original = {"Serilog":{"MinimumLevel":{"Default":"Warning","Override":{"Other":"Error"}},
                               "WriteTo":[{"Name":"File","Args":{"outputTemplate":"unchanged"}}]}}
        edited = json.loads(host.HostOperation.override(json.dumps(original).encode()))
        self.assertEqual("Debug", edited["Serilog"]["MinimumLevel"]["Override"].pop(host.CATEGORY))
        self.assertEqual(original, edited)


    def test_deletes_only_matching_plugins_and_the_plugin_cache_tree(self):
        import json
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            plugins = folder / "plugins"
            matching = plugins / "old"
            unrelated = plugins / "unrelated"
            for path, identity in ((matching, str(host.PLUGIN_ID)), (unrelated, "00000000-0000-0000-0000-000000000001")):
                path.mkdir(parents=True)
                (path / "meta.json").write_text(json.dumps({"guid": identity}))
            cache = folder / "cache"
            cache.mkdir()
            (cache / "old.jpg").write_bytes(b"old")
            outside = folder / "other-cache.jpg"
            outside.write_bytes(b"preserve")
            binary = folder / "build"
            binary.mkdir()
            for suffix in ("dll", "pdb"):
                (binary / (host.ASSEMBLY + "." + suffix)).write_bytes(suffix.encode())
            logging = folder / "logging.json"
            logging.write_text('{"Serilog":{"MinimumLevel":"Information"}}')
            operation = host.HostOperation(logging, plugins, cache, lambda: None)
            self.assertEqual(0, operation.prepare(binary, "1.0.0.0"))
            self.assertFalse(matching.exists())
            self.assertTrue(unrelated.exists())
            self.assertEqual([], list(cache.iterdir()))
            self.assertEqual(b"preserve", outside.read_bytes())

    def test_symlinked_cache_does_not_escape_the_mutation_boundary(self):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            real = folder / "unrelated"
            real.mkdir()
            sentinel = real / "keep"
            sentinel.write_bytes(b"preserve")
            linked = folder / "cache"
            linked.symlink_to(real, target_is_directory=True)
            operation = host.HostOperation(folder / "logging.json", folder / "plugins", linked, lambda: None)
            with self.assertRaises(ValueError):
                operation.deploy(folder, "1.0.0.0")
            self.assertEqual(b"preserve", sentinel.read_bytes())


    def test_snapshot_is_claimed_before_reading_original_logging(self):
        import threading
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            logging = folder / "logging.json"
            logging.write_text('{"Serilog":{"MinimumLevel":"Information"}}')
            for suffix in ("dll", "pdb"):
                (folder / (host.ASSEMBLY + "." + suffix)).write_bytes(suffix.encode())
            operation = host.HostOperation(logging, folder / "plugins", folder / "cache", lambda: None)
            reading = threading.Event()
            release = threading.Event()
            read = pathlib.Path.read_bytes
            result = []
            def blocked_read(path):
                if path == logging and threading.current_thread() is worker:
                    reading.set()
                    if not release.wait(5):
                        raise TimeoutError("barrier")
                return read(path)
            worker = threading.Thread(target=lambda: result.append(operation.prepare(folder, "1.0.0.0")))
            with patch.object(pathlib.Path, "read_bytes", blocked_read):
                worker.start()
                try:
                    self.assertTrue(reading.wait(5))
                    self.assertEqual(host.REFUSED, operation.prepare(folder, "1.0.0.0"))
                finally:
                    release.set()
                    worker.join(5)
            self.assertEqual([0], result)
            self.assertEqual(0, operation.restore())

    def test_incomplete_snapshot_metadata_never_authorizes_restoration(self):
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            logging = folder / "logging.json"
            original = b'{"Serilog":{"MinimumLevel":"Information"}}'
            logging.write_bytes(original)
            for suffix in ("dll", "pdb"):
                (folder / (host.ASSEMBLY + "." + suffix)).write_bytes(suffix.encode())
            operation = host.HostOperation(logging, folder / "plugins", folder / "cache", lambda: self.fail("restart"))
            with patch.object(host.shutil, "copystat", side_effect=OSError("metadata failure")):
                self.assertEqual(host.BEFORE_SNAPSHOT, operation.prepare(folder, "1.0.0.0"))
            self.assertEqual(original, logging.read_bytes())
            self.assertTrue(operation.snapshot.exists())
            self.assertFalse((folder / "plugins").exists())

    def test_query_sandbox_reads_live_wal_without_writing_any_state(self):
        import sqlite3
        import subprocess
        import sys
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            database = folder / "test.db"
            connection = sqlite3.connect(database)
            try:
                connection.execute("PRAGMA journal_mode=WAL")
                connection.execute("CREATE TABLE subject(id INTEGER)")
                connection.execute("INSERT INTO subject VALUES(75)")
                connection.commit()
                before = {path.name: path.read_bytes() for path in folder.iterdir()}
                code = """
import pathlib,sqlite3,sys
sys.path.insert(0,sys.argv[1])
from read_only_filesystem import restrict_writes
restrict_writes()
folder=pathlib.Path(sys.argv[2])
try:
    (folder/'forbidden').write_bytes(b'no')
    raise AssertionError('writes were allowed')
except PermissionError:
    pass
with sqlite3.connect((folder/'test.db').as_uri()+'?mode=ro',uri=True) as db:
    assert db.execute('SELECT id FROM subject').fetchone() == (75,)
"""
                result = subprocess.run([sys.executable, "-B", "-c", code, str(HARNESS_DIRECTORY), root],
                                        capture_output=True, text=True, timeout=10)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(before, {path.name: path.read_bytes() for path in folder.iterdir()})
            finally:
                connection.close()


if __name__ == "__main__":
    unittest.main()
