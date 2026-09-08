from __future__ import annotations

import pathlib
import subprocess
import sys

import tiktoken


MAXIMUM_PHYSICAL_LINES = 500
MAXIMUM_CODE_MAP_TOKENS = 1_000
SOURCE_SUFFIXES = {
    ".awk",
    ".bash",
    ".bat",
    ".c",
    ".cake",
    ".cc",
    ".cmd",
    ".cpp",
    ".cs",
    ".csproj",
    ".csx",
    ".dart",
    ".fish",
    ".fs",
    ".fsx",
    ".go",
    ".gradle",
    ".groovy",
    ".h",
    ".hh",
    ".hpp",
    ".java",
    ".js",
    ".jsx",
    ".kt",
    ".kts",
    ".lua",
    ".mjs",
    ".nu",
    ".php",
    ".pl",
    ".pm",
    ".ps1",
    ".psd1",
    ".psm1",
    ".py",
    ".pyw",
    ".r",
    ".rb",
    ".rs",
    ".scala",
    ".sed",
    ".sh",
    ".sql",
    ".swift",
    ".targets",
    ".tcl",
    ".ts",
    ".tsx",
    ".vb",
    ".zsh",
    ".props",
}
SCRIPT_FILE_NAMES = {
    "Containerfile",
    "Dockerfile",
    "GNUmakefile",
    "Justfile",
    "Makefile",
    "Taskfile",
    "makefile",
}
WORKFLOW_SUFFIXES = {".yaml", ".yml"}


def tracked_files() -> list[tuple[pathlib.Path, bool]]:
    output = subprocess.check_output(["git", "ls-files", "--stage", "-z"])
    files: list[tuple[pathlib.Path, bool]] = []
    for raw_record in output.split(b"\0"):
        if not raw_record:
            continue
        metadata, raw_path = raw_record.split(b"\t", maxsplit=1)
        mode = metadata.split(maxsplit=1)[0]
        files.append((pathlib.Path(raw_path.decode("utf-8")), mode == b"100755"))
    return files


def is_size_limited(path: pathlib.Path, data: bytes, executable: bool) -> bool:
    if executable or data.startswith(b"#!"):
        return True
    if path.name in SCRIPT_FILE_NAMES or path.suffix.lower() in SOURCE_SUFFIXES:
        return True
    if path.parts[:2] == (".github", "workflows"):
        return path.suffix.lower() in WORKFLOW_SUFFIXES
    return False


def main() -> int:
    tracked = tracked_files()
    failures: list[str] = []

    for relative_path, executable in tracked:
        name = relative_path.as_posix()
        data = relative_path.read_bytes()
        if not is_size_limited(relative_path, data, executable):
            continue
        physical_lines = data.count(b"\n")
        if data and not data.endswith(b"\n"):
            physical_lines += 1
        if physical_lines > MAXIMUM_PHYSICAL_LINES:
            failures.append(
                f"{name}: {physical_lines} physical lines; "
                f"maximum is {MAXIMUM_PHYSICAL_LINES}."
            )

    if tiktoken.__version__ != "0.12.0":
        failures.append(f"Tokenizer version is {tiktoken.__version__}; expected 0.12.0.")
    encoding = tiktoken.get_encoding("cl100k_base")
    code_maps = sorted(pathlib.Path("docs/code-maps").rglob("*.md"))
    for code_map in code_maps:
        source = code_map.read_text(encoding="utf-8")
        token_count = len(encoding.encode(source, disallowed_special=()))
        if token_count > MAXIMUM_CODE_MAP_TOKENS:
            failures.append(
                f"{code_map.as_posix()}: {token_count} cl100k_base tokens; "
                f"maximum is {MAXIMUM_CODE_MAP_TOKENS}."
            )

    if failures:
        print("Repository structure contract violations:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1

    print(f"Verified {len(tracked)} tracked paths and {len(code_maps)} Code Maps.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
