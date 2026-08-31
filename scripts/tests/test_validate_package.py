from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR = REPOSITORY_ROOT / "scripts" / "validate_package.py"


class ValidatePackageTests(unittest.TestCase):
    def test_rejects_forbidden_archive_members(self) -> None:
        forbidden_members = [
            "Jellyfin.Plugin.TrickplayCropper.pdb",
            "nested/Jellyfin.Plugin.TrickplayCropper.dll",
            "Jellyfin.Controller.dll",
            "SkiaSharp.dll",
            "libSkiaSharp.so",
            "SkiaSharp.NativeAssets.Linux.NoDependencies.dll",
            "runtimes/linux-x64/native/libSkiaSharp.so",
        ]

        for forbidden_member in forbidden_members:
            with self.subTest(forbidden_member=forbidden_member):
                result = self._run_validator(
                {
                    "Jellyfin.Plugin.TrickplayCropper.dll": b"assembly",
                    forbidden_member: b"forbidden",
                    "meta.json": self._valid_metadata(),
                }
            )

                self.assertNotEqual(0, result.returncode)
                self.assertIn(forbidden_member, result.stderr)

    def test_rejects_incorrect_metadata(self) -> None:
        incorrect_values = {
            "name": "Another Plugin",
            "guid": "00000000-0000-0000-0000-000000000000",
            "version": "2.0.0.0",
            "targetAbi": "10.12.0.0",
        }

        for key, incorrect_value in incorrect_values.items():
            with self.subTest(key=key):
                metadata = json.loads(self._valid_metadata())
                metadata[key] = incorrect_value
                result = self._run_validator(
                    {
                        "Jellyfin.Plugin.TrickplayCropper.dll": b"assembly",
                        "meta.json": json.dumps(metadata).encode(),
                    }
                )

                self.assertNotEqual(0, result.returncode)
                self.assertIn(key, result.stderr)

    def test_accepts_the_install_contract(self) -> None:
        result = self._run_validator(
            {
                "Jellyfin.Plugin.TrickplayCropper.dll": b"assembly",
                "meta.json": self._valid_metadata(),
            }
        )

        self.assertEqual(0, result.returncode, result.stderr)

    def _run_validator(self, members: dict[str, bytes]) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory() as temporary_directory:
            archive = Path(temporary_directory) / "plugin.zip"
            self._write_archive(archive, members)

            return subprocess.run(
                [sys.executable, VALIDATOR, archive],
                check=False,
                capture_output=True,
                text=True,
            )

    @staticmethod
    def _write_archive(archive: Path, members: dict[str, bytes]) -> None:
        with zipfile.ZipFile(archive, mode="w") as package:
            for name, contents in members.items():
                package.writestr(name, contents)

    @staticmethod
    def _valid_metadata() -> bytes:
        return json.dumps(
            {
                "name": "Trickplay Cropper",
                "guid": "630fb758-9a29-4f2c-a54c-95793651bb8a",
                "version": "1.0.0.0",
                "targetAbi": "10.11.0.0",
                "framework": "net9.0",
            }
        ).encode()


if __name__ == "__main__":
    unittest.main()
