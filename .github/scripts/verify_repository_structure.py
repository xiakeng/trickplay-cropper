from __future__ import annotations

import pathlib
import re
import subprocess
import sys
import urllib.parse

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

# Exclusions must remain exact Git-tracked paths categorized as generated or
# third-party. The repository currently needs no exclusions.
EXCLUDED_PATHS: dict[str, str] = {}
ALLOWED_EXCLUSION_CATEGORIES = {"generated", "third-party"}


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


def local_markdown_targets(source: str) -> list[str]:
    inline_link = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")
    reference_definition = re.compile(
        r"(?m)^[ \t]{0,3}\[[^\]]+\]:[ \t]*(?:<([^>]+)>|(\S+))"
    )
    targets = inline_link.findall(source)
    targets.extend(left or right for left, right in reference_definition.findall(source))
    return targets


def main() -> int:
    root = pathlib.Path.cwd().resolve()
    tracked = tracked_files()
    tracked_names = {path.as_posix() for path, _ in tracked}
    failures: list[str] = []

    for path, category in EXCLUDED_PATHS.items():
        if category not in ALLOWED_EXCLUSION_CATEGORIES:
            failures.append(
                f"Invalid exclusion category for {path}: {category}; "
                "expected generated or third-party."
            )
        if path not in tracked_names:
            failures.append(f"Excluded path is not Git-tracked: {path}")

    for relative_path, executable in tracked:
        name = relative_path.as_posix()
        if name in EXCLUDED_PATHS:
            continue
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
        token_count = len(encoding.encode(source))
        if token_count > MAXIMUM_CODE_MAP_TOKENS:
            failures.append(
                f"{code_map.as_posix()}: {token_count} cl100k_base tokens; "
                f"maximum is {MAXIMUM_CODE_MAP_TOKENS}."
            )

        for raw_target in local_markdown_targets(source):
            target = raw_target.strip()
            if target.startswith("<") and target.endswith(">"):
                target = target[1:-1]
            parsed = urllib.parse.urlparse(target)
            if parsed.scheme or target.startswith("#"):
                continue
            link_path = urllib.parse.unquote(parsed.path)
            if not link_path:
                continue
            candidate = (
                root / link_path.lstrip("/")
                if link_path.startswith("/")
                else code_map.parent / link_path
            ).resolve()
            try:
                candidate.relative_to(root)
            except ValueError:
                failures.append(
                    f"{code_map.as_posix()}: local link escapes the repository: {target}"
                )
                continue
            if not candidate.exists():
                failures.append(
                    f"{code_map.as_posix()}: local link target does not exist: {target}"
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
