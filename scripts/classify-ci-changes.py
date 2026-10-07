#!/usr/bin/env python3
"""CI-P1: classify changed paths conservatively without network/API access.

The PR-merge mode diffs HEAD^1..HEAD, where HEAD is the checkout of GitHub's
synthetic PR merge commit. If this relationship is unavailable, select the full
build/test surface; never silently skip verification.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
from pathlib import Path

SOURCE_EXTENSIONS = {".cs", ".csproj", ".sln", ".slnx", ".xaml", ".props", ".targets",
                     ".resx", ".razor", ".fs", ".fsproj", ".config", ".manifest"}
DOC_EXTENSIONS = {".md", ".rst", ".adoc", ".txt"}
WEBSITE_EXTENSIONS = {".html", ".css", ".js", ".mjs", ".png", ".jpg", ".jpeg",
                      ".webp", ".svg", ".gif", ".ico", ".woff", ".woff2"}
SCL_TOKENS = ("scl", "mms", "iec61850", "discovery", "report", "rcb", "control",
              "dataset", "fcda", "ldmodel", "ied", "sasi")
SV_TOKENS = ("smv", "sampledvalue", "sampled-value", "processbus", "goose", "sv-evidence")
IO_TOKENS = ("iotest", "iolist", "iofat", "fatdataset", "fatscl", "io-testing")


def classify(paths: list[str]) -> dict:
    """Pure classifier. Unknown files intentionally require the broad build."""
    def normalize(name: str) -> str:
        normalized = name.replace("\\", "/")
        while normalized.startswith("./"):
            normalized = normalized[2:]
        return normalized

    changed = sorted({normalize(name) for name in paths if name.strip()})
    areas: set[str] = set()
    build = tests = iec = sv = io = packaging = website = ci = unknown = False

    if not changed:
        # A missing/unsupported comparison is never interpreted as a docs-only change.
        changed = ["__unclassified_source_change__"]

    for path in changed:
        lower = path.lower()
        basename = Path(lower).name
        suffix = Path(lower).suffix
        if path == "__unclassified_source_change__":
            areas.add("unknown")
            build = tests = iec = sv = io = packaging = unknown = True
            continue

        if lower.startswith("landing/") or lower.startswith("website/"):
            areas.add("website")
            website = True
            continue

        if lower.startswith(".github/"):
            areas.add("ci")
            ci = True
            if not (lower.startswith(".github/workflows/") or
                    lower.startswith(".github/actions/")):
                build = tests = True
            # CI-only changes are covered by the P0 guard and the opt-in P1 shadow.
            continue

        if lower.startswith("docs/") and lower != "docs/interoperability_reference_contract.md":
            areas.add("docs")
            continue

        if lower.startswith("scripts/"):
            areas.add("ci")
            ci = True
            if basename not in {
                "classify-ci-changes.py",
                "test-classify-ci-changes.py",
                "audit-ci-workflows.py",
                "test-ci-workflows.py",
            }:
                build = tests = True  # scripts can affect release/build semantics
            continue

        if lower.startswith("evidence/"):
            areas.add("authority")
            iec = True
            build = tests = True  # existing authority workflows remain mandatory
            continue

        if lower.startswith("engines/") or lower.startswith("engine-patches/"):
            areas.add("engine")
            build = tests = iec = sv = io = True
            continue

        if lower.startswith("installer/") or lower.startswith(".release/") or lower == "version":
            areas.add("packaging")
            packaging = True
            build = tests = True
            continue

        if lower == "docs/interoperability_reference_contract.md":
            areas.add("authority")
            iec = True
            build = tests = True
            continue

        if lower.startswith("assets/") or lower.startswith("resources/"):
            areas.add("app")
            build = tests = True
            continue

        if suffix in SOURCE_EXTENSIONS or lower in {
            "global.json", "nuget.config", "directory.build.props",
            "directory.build.targets", "directory.packages.props", "app.manifest"
        }:
            areas.add("app")
            build = tests = True
            if any(token in lower for token in SCL_TOKENS):
                iec = True
                areas.add("iec61850")
            if any(token in lower for token in SV_TOKENS):
                sv = True
                areas.add("process-bus")
            if any(token in lower for token in IO_TOKENS):
                io = True
                areas.add("io-fat")
            continue

        if lower.startswith("tests/"):
            # Non-source test assets can change regression truth.
            areas.add("tests")
            build = tests = True
            continue

        if suffix in DOC_EXTENSIONS or lower in {"readme", "license", "notice"}:
            areas.add("docs")
            continue

        if suffix in WEBSITE_EXTENSIONS:
            areas.add("website")
            website = True
            continue

        # Unclassified paths can affect the build, packaging, or evidence.
        areas.add("unknown")
        build = tests = iec = sv = io = packaging = unknown = True

    flags = {
        "build": build,
        "tests": tests,
        "iec": iec,
        "sv": sv,
        "io": io,
        "packaging": packaging,
        "website": website,
        "ci": ci,
        "unknown": unknown,
    }
    risk = "broad" if unknown or "engine" in areas else ("targeted" if build else "none")
    return {
        "schemaVersion": 1,
        "areas": sorted(areas),
        "changedFiles": changed,
        "flags": flags,
        "risk": risk,
        "requiresLegacyAuthority": bool(iec or packaging),
        "interpretation": (
            "Advisory classification only. Legacy CI remains authoritative; "
            "unknown input always selects broad verification."
        ),
    }


def merge_diff(root: Path) -> list[str]:
    """Collect PR merge-tree changes; absence of a merge parent is a full-build signal."""
    try:
        parents = subprocess.check_output(
            ["git", "-C", str(root), "rev-list", "--parents", "-n", "1", "HEAD"],
            stderr=subprocess.PIPE,
        ).decode("ascii", "strict").split()
        if len(parents) != 3:
            return ["__unclassified_source_change__"]

        raw = subprocess.check_output(
            ["git", "-C", str(root), "diff", "--name-only", "-z", parents[1], "HEAD"],
            stderr=subprocess.PIPE,
        )
        return [name.decode("utf-8", "surrogateescape")
                for name in raw.split(b"\0") if name] or ["__unclassified_source_change__"]
    except (subprocess.CalledProcessError, UnicodeError, OSError):
        return ["__unclassified_source_change__"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--from-pr-merge", action="store_true")
    parser.add_argument("--paths", nargs="*", default=None)
    parser.add_argument("--github-output", type=Path)
    parser.add_argument("--summary", type=Path)
    parser.add_argument("--json-output", type=Path)
    args = parser.parse_args()

    report = classify(
        merge_diff(args.root.resolve()) if args.from_pr_merge else (args.paths or [])
    )
    payload = json.dumps(report, sort_keys=True, ensure_ascii=False)
    print(payload)

    if args.github_output:
        with args.github_output.open("a", encoding="utf-8") as stream:
            for key, value in sorted(report["flags"].items()):
                stream.write(f"{key}={str(value).lower()}\n")
            stream.write(f"risk={report['risk']}\n")

    if args.json_output:
        args.json_output.write_text(payload + "\n", encoding="utf-8")

    if args.summary:
        with args.summary.open("a", encoding="utf-8") as stream:
            stream.write("## CI-P1 advisory change classifier\n\n")
            stream.write(f"- areas: {', '.join(report['areas'])}\n")
            stream.write(f"- risk: {report['risk']}\n")
            stream.write(f"- full Windows build required: {report['flags']['build']}\n")
            stream.write("- legacy authority remains required and unchanged\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
