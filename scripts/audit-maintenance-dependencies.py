#!/usr/bin/env python3
"""Read-only, deterministic inventory of Git-tracked workflow/script references.

A textual reference does not prove execution. No reference does not prove a file
is unused. This report must never be used as an automatic deletion list.
"""
from __future__ import annotations
import argparse
import json
import re
import subprocess
from pathlib import Path

TEXT_EXT = frozenset((".cs", ".csproj", ".props", ".targets", ".sln", ".slnx",
    ".md", ".txt", ".ps1", ".py", ".bat", ".cmd", ".yml", ".yaml", ".json",
    ".xml", ".xaml", ".iss", ".html", ".js", ".css", ".tmpl", ".cfg"))
SCRIPT_EXT = frozenset((".py", ".ps1", ".bat", ".cmd"))
NAMES = frozenset(("CODEOWNERS", "LICENSE", "NOTICE", "VERSION", ".editorconfig",
    ".gitignore", ".gitattributes"))

def git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(("git", "-C", str(root), *args), stderr=subprocess.PIPE)

def paths(root: Path) -> list[str]:
    return sorted({p.decode("utf-8", "surrogateescape").replace("\\", "/")
                   for p in git(root, "ls-files", "-z").split(b"\0") if p})

def events(content: str) -> list[str]:
    found: set[str] = set()
    in_on = False
    for line in content.splitlines():
        if re.match(r"^on:\s*(?:#.*)?$", line):
            in_on = True
            continue
        inline = re.match(r"^on:\s*\[([^]]+)\]", line)
        if inline:
            found.update(x.strip().strip("'\"") for x in inline.group(1).split(",") if x.strip())
            break
        if in_on and line and not line[0].isspace() and not line.lstrip().startswith("#"):
            break
        if in_on:
            match = re.match(r"^  ([\w-]+):(?:\s|$)", line)
            if match:
                found.add(match.group(1))
    return sorted(found)

def inventory(root: Path) -> dict:
    tracked = paths(root)
    scripts = sorted(p for p in tracked if p.startswith("scripts/") and
                     Path(p).suffix.lower() in SCRIPT_EXT)
    workflows = sorted(p for p in tracked if p.startswith(".github/workflows/") and
                       Path(p).suffix.lower() in (".yml", ".yaml"))
    patterns = {p: re.compile(r"(?<![\w.\-])(?:scripts[\\/])?" +
                re.escape(Path(p).name) + r"(?![\w\-]|\.[\w\-])", re.IGNORECASE)
                for p in scripts}
    refs: dict[str, list[str]] = {p: [] for p in scripts}
    trigger_map: dict[str, list[str]] = {}
    scanned = 0
    for path in tracked:
        if Path(path).suffix.lower() not in TEXT_EXT and Path(path).name not in NAMES:
            continue
        full = root / path
        if not full.is_file():
            raise FileNotFoundError("Tracked text file missing: " + path)
        content = full.read_text(encoding="utf-8-sig", errors="replace")
        scanned += 1
        if path in workflows:
            trigger_map[path] = events(content)
        for script, pattern in patterns.items():
            if path != script and pattern.search(content):
                refs[script].append(path)
    result = {
        "schemaVersion": 1,
        "sourceCommit": git(root, "rev-parse", "HEAD").decode("ascii").strip(),
        "scope": "Tracked textual references only; manual, dynamic and external consumers require review",
        "counts": {"trackedPaths": len(tracked), "scannedTextFiles": scanned,
                   "workflows": len(workflows), "scripts": len(scripts)},
        "workflows": [
            {"path": p, "events": trigger_map.get(p, []),
             "referencedScripts": sorted(s for s in scripts if p in refs[s])}
            for p in workflows],
        "scripts": [
            {"path": p, "workflowReferences": sorted(x for x in refs[p] if x in workflows),
             "otherTrackedReferences": sorted(x for x in refs[p] if x not in workflows)}
            for p in scripts],
        "requiresManualConsumerReview": sorted(p for p in scripts if not refs[p]),
        "interpretation": "No reference does NOT mean unused. Verify manual invocation, dispatch, external consumers, archived release evidence and dynamic calls before retirement."
    }
    return result

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = json.dumps(inventory(args.root.resolve()), indent=2, ensure_ascii=False) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(report, encoding="utf-8")
    else:
        print(report, end="")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
