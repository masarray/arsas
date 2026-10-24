#!/usr/bin/env python3
"""Fail-closed FAT EXE release input verification; never publishes anything."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

SHA40 = re.compile(r"[a-f0-9]{40}\Z")
RUNS = {"Build ARSAS", "Validate ARSAS Windows installer"}


def validate_run(run: dict, sha: str, workflow: str) -> None:
    if not SHA40.fullmatch(sha):
        raise ValueError("Expected exact 40-character lowercase source SHA")
    if workflow not in RUNS:
        raise ValueError("Unrecognized verified Windows workflow")
    if (run.get("head_sha") != sha or run.get("name") != workflow
            or run.get("status") != "completed"
            or run.get("conclusion") != "success"):
        raise ValueError("Run is unqualified or is not the exact requested revision")
    if run.get("event") not in {"pull_request", "push", "workflow_dispatch"}:
        raise ValueError("Unrecognized source-build event")


def check_exe(path: Path) -> str:
    if not path.is_file() or path.stat().st_size < 1024 * 1024:
        raise ValueError("Missing or unexpectedly small Windows executable")
    with path.open("rb") as stream:
        header = stream.read(64)
        if len(header) != 64 or header[:2] != b"MZ":
            raise ValueError("File is not a Windows PE executable")
        pe_offset = int.from_bytes(header[0x3C:0x40], "little")
        if pe_offset < 64 or pe_offset > 1024 * 1024:
            raise ValueError("Invalid Windows PE header offset")
        stream.seek(pe_offset)
        if stream.read(4) != b"PE\x00\x00":
            raise ValueError("Missing Windows PE signature")
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main() -> None:
    cli = argparse.ArgumentParser()
    cli.add_argument("--run-json", type=Path, required=True)
    cli.add_argument("--sha", required=True)
    cli.add_argument("--workflow", choices=sorted(RUNS), required=True)
    cli.add_argument("--exe", type=Path, required=True)
    cli.add_argument("--output", type=Path, required=True)
    args = cli.parse_args()
    run = json.loads(args.run_json.read_text(encoding="utf-8"))
    validate_run(run, args.sha, args.workflow)
    digest = check_exe(args.exe)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({
        "testedSourceSha": args.sha, "workflow": args.workflow,
        "workflowRunId": run.get("id"), "buildEvent": run.get("event"),
        "file": args.exe.name, "fileSizeBytes": args.exe.stat().st_size,
        "sha256": digest, "channel": "public-prerelease-FAT-only",
        "fieldAcceptance": False, "productionPromotion": False,
    }, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
