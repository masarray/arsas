#!/usr/bin/env python3
"""Stamp build-info.json with immutable deployment provenance."""

from __future__ import annotations

import argparse
import json
import re
from datetime import datetime
from pathlib import Path

SHA_PATTERN = re.compile(r"[0-9a-f]{40}", re.IGNORECASE)


def iso_timestamp(value: str) -> str:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise SystemExit(f"Invalid commit timestamp: {value}") from exc
    return parsed.isoformat()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("site", nargs="?", default="_site")
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--source-ref", required=True)
    parser.add_argument("--commit-timestamp", required=True)
    parser.add_argument("--workflow-run-id", required=True)
    parser.add_argument("--workflow-run-attempt", default="1")
    args = parser.parse_args()

    site = Path(args.site).resolve()
    path = site / "build-info.json"
    if not path.is_file():
        raise SystemExit(f"Missing build-info.json: {path}")
    source_commit = args.source_commit.strip().lower()
    if not SHA_PATTERN.fullmatch(source_commit):
        raise SystemExit("source commit must be a full 40-character Git SHA")
    if not args.source_ref.strip():
        raise SystemExit("source ref is required")
    commit_timestamp = iso_timestamp(args.commit_timestamp.strip())
    if not str(args.workflow_run_id).strip():
        raise SystemExit("workflow run ID is required")

    payload = json.loads(path.read_text(encoding="utf-8"))
    payload["sourceCommit"] = source_commit
    payload["sourceRef"] = args.source_ref.strip()
    payload["buildTimestampUtc"] = commit_timestamp
    payload["workflowRunId"] = str(args.workflow_run_id).strip()
    payload["workflowRunAttempt"] = str(args.workflow_run_attempt).strip() or "1"
    payload["deploymentAttestation"] = {
        "provider": "github-pages",
        "sourceCommit": source_commit,
        "sourceRef": args.source_ref.strip(),
        "commitTimestampUtc": commit_timestamp,
        "workflowRunId": str(args.workflow_run_id).strip(),
        "workflowRunAttempt": str(args.workflow_run_attempt).strip() or "1",
    }
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Stamped ARSAS website build with source commit {source_commit}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
