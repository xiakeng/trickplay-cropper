#!/usr/bin/env python3
"""Validate the manually installable Trickplay Cropper package."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
import zipfile


EXPECTED_MEMBERS = {
    "Jellyfin.Plugin.TrickplayCropper.dll",
    "meta.json",
}
EXPECTED_METADATA = {
    "guid": "630fb758-9a29-4f2c-a54c-95793651bb8a",
    "name": "Trickplay Cropper",
    "targetAbi": "10.11.0.0",
    "version": "1.0.0.0",
}


class PackageValidationError(Exception):
    """Raised when a plugin package violates the installation contract."""


def validate_package(package_path: Path) -> None:
    """Validate one JPRM-produced ZIP package."""
    with zipfile.ZipFile(package_path) as package:
        members = package.namelist()
        member_set = set(members)
        unexpected = sorted(member_set - EXPECTED_MEMBERS)
        missing = sorted(EXPECTED_MEMBERS - member_set)

        if unexpected:
            raise PackageValidationError(
                f"Unexpected archive members: {', '.join(unexpected)}"
            )

        if missing:
            raise PackageValidationError(
                f"Missing archive members: {', '.join(missing)}"
            )

        if len(members) != len(EXPECTED_MEMBERS):
            raise PackageValidationError("Duplicate archive members are not allowed")

        try:
            metadata = json.loads(package.read("meta.json"))
        except (json.JSONDecodeError, UnicodeDecodeError) as error:
            raise PackageValidationError(f"Invalid meta.json: {error}") from error

        if not isinstance(metadata, dict):
            raise PackageValidationError("meta.json must contain a JSON object")

        for key, expected_value in EXPECTED_METADATA.items():
            actual_value = metadata.get(key)
            if actual_value != expected_value:
                raise PackageValidationError(
                    f"meta.json {key} must be {expected_value!r}, got {actual_value!r}"
                )


def parse_arguments() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path, help="Path to the plugin ZIP")
    return parser.parse_args()


def main() -> int:
    """Run package validation for the command-line interface."""
    arguments = parse_arguments()

    try:
        validate_package(arguments.package)
    except (OSError, PackageValidationError, zipfile.BadZipFile) as error:
        print(f"Package validation failed: {error}", file=sys.stderr)
        return 1

    print(f"Package validation passed: {arguments.package}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
