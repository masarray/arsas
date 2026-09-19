#!/usr/bin/env python3
"""Fail closed when release-sensitive ARSAS inputs drift from qualification."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path


MANIFEST_PATH = Path(".release/qualified-source.json")
WINDOWS_MANIFEST_PATH = Path(".release/windows.json")
NORMALIZED_VERSION_PATHS = {
    "ArIED61850Tester.csproj",
    "Directory.Build.props",
    "VERSION",
}
EXCLUDED_PREFIXES = (
    ".github/",
    ".release/",
    "docs/",
    "evidence/",
    "landing/",
    "tests/",
)
INCLUDED_GOVERNANCE_PATHS = {
    ".github/workflows/release-windows.yml",
}
EXCLUDED_SUFFIXES = (
    ".md",
)
VERSION_ELEMENT_PATTERN = re.compile(
    rb"(<(?:Version|AssemblyVersion|FileVersion|InformationalVersion)>)[^<]*(</(?:Version|AssemblyVersion|FileVersion|InformationalVersion)>)"
)


def repository_root() -> Path:
    return Path(__file__).resolve().parents[1]


def tracked_paths(root: Path) -> list[str]:
    result = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-z"],
        check=True,
        stdout=subprocess.PIPE,
    )
    return sorted(
        item.decode("utf-8")
        for item in result.stdout.split(b"\0")
        if item
    )


def is_release_sensitive(path: str) -> bool:
    if path in INCLUDED_GOVERNANCE_PATHS:
        return True
    if path.startswith(EXCLUDED_PREFIXES):
        return False
    if path.endswith(EXCLUDED_SUFFIXES):
        return False
    return True


def normalized_content(path: str, content: bytes) -> bytes:
    if path == "VERSION":
        return b"<QUALIFIED_VERSION>\n"
    if path in NORMALIZED_VERSION_PATHS:
        return VERSION_ELEMENT_PATTERN.sub(
            rb"\1<QUALIFIED_VERSION>\2",
            content,
        )
    return content


def calculate_fingerprint(root: Path) -> tuple[str, list[str]]:
    digest = hashlib.sha256()
    selected: list[str] = []
    for path in tracked_paths(root):
        if not is_release_sensitive(path):
            continue
        full_path = root / path
        if not full_path.is_file():
            raise RuntimeError(f"Tracked release-sensitive path is missing: {path}")
        selected.append(path)
        digest.update(path.encode("utf-8"))
        digest.update(b"\0")
        digest.update(normalized_content(path, full_path.read_bytes()))
        digest.update(b"\0")
    return digest.hexdigest(), selected


def load_json(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"Unable to read valid JSON from {path}: {exc}") from exc


def validate_manifest(root: Path, fingerprint: str, selected: list[str]) -> list[str]:
    manifest = load_json(root / MANIFEST_PATH)
    windows_manifest = load_json(root / WINDOWS_MANIFEST_PATH)
    errors: list[str] = []

    if manifest.get("schemaVersion") != 1:
        errors.append("Qualification manifest schemaVersion must be 1.")
    if windows_manifest.get("qualificationManifest") != MANIFEST_PATH.as_posix():
        errors.append("Windows release manifest is not bound to the qualification manifest.")

    source = manifest.get("fieldQualifiedSource") or {}
    required_source_values = {
        "sourcePullRequest": 324,
        "sourceHeadCommit": "b0f25569a398c7bbdc3a3a34409dbd74ebf49444",
        "sourceMergeCommit": "2b8e8adfd019bd087dc338dcc32bffefec53370f",
        "sourceTree": "cccb50607161f8637e597b43d71038ffd4becfdc",
        "workflowRunId": 35412542175,
        "artifactId": 10575075419,
        "portableSha256": "6805b878f91f3d526cc50f3e81e3eb9a1f72500217dacc8921e50bdc349c5fcc",
        "portableSizeBytes": 78383289,
    }
    for key, expected in required_source_values.items():
        if source.get(key) != expected:
            errors.append(f"Qualified source {key} does not match the approved authority.")

    candidate = manifest.get("currentReleaseCandidate") or {}
    if candidate.get("fingerprintAlgorithm") != "sha256-path-null-content-null-v1":
        errors.append("Unsupported release-sensitive fingerprint algorithm.")
    if candidate.get("releaseSensitiveFileCount") != len(selected):
        errors.append(
            "Release-sensitive file count drifted: "
            f"expected {candidate.get('releaseSensitiveFileCount')}, actual {len(selected)}."
        )
    if candidate.get("releaseSensitiveFingerprint") != fingerprint:
        errors.append(
            "Release-sensitive source drifted from the qualified candidate: "
            f"expected {candidate.get('releaseSensitiveFingerprint')}, actual {fingerprint}."
        )
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--print",
        action="store_true",
        dest="print_only",
        help="Print the calculated fingerprint and file count without verifying.",
    )
    args = parser.parse_args()

    root = repository_root()
    fingerprint, selected = calculate_fingerprint(root)
    if args.print_only:
        print(
            json.dumps(
                {
                    "releaseSensitiveFileCount": len(selected),
                    "releaseSensitiveFingerprint": fingerprint,
                },
                indent=2,
            )
        )
        return 0

    errors = validate_manifest(root, fingerprint, selected)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    print(
        "Qualified release source PASS: "
        f"{len(selected)} files, fingerprint {fingerprint}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
