#!/usr/bin/env python3
"""Produce exact-SHA canonical Build ARSAS regression proof from verified TRX.

This script runs only after the canonical full test step succeeded. It writes
into the already-uploaded ARSAS-test-evidence artifact; there is no new artifact
publication surface and no release authority change.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path


def sha(value: str, label: str) -> str:
    value = (value or "").lower()
    if len(value) != 40 or any(ch not in "0123456789abcdef" for ch in value):
        raise ValueError(f"{label} must be an immutable lowercase 40-hex SHA")
    return value


def git_head(path: Path) -> str:
    return sha(
        subprocess.check_output(
            ["git", "-C", str(path), "rev-parse", "HEAD"], stderr=subprocess.PIPE
        ).decode("ascii").strip(),
        "verified checkout",
    )


def counters_from_trx(data: bytes) -> dict[str, int]:
    root = ET.fromstring(data)
    counters = root.find(".//{*}Counters")
    if counters is None:
        raise ValueError("TRX has no test-result Counters element")

    result = {}
    for name in ("total", "passed", "failed", "notExecuted", "executed"):
        value = int(counters.attrib.get(name, -1))
        if value < 0:
            raise ValueError(f"TRX Counters missing valid {name}")
        result[name] = value
    if (
        result["total"] <= 0
        or result["failed"] != 0
        or result["notExecuted"] != 0
        or result["passed"] != result["total"]
        or result["executed"] != result["total"]
    ):
        raise ValueError(
            f"Canonical full suite is not entirely green: {result}"
        )
    return result


def create_manifest(
    source_sha: str,
    checkout_sha: str,
    engine_expected: str,
    engine_actual: str,
    workflow_run_id: int,
    run_attempt: int,
    event_name: str,
    trx: bytes,
    trx_name: str,
) -> dict:
    for label, value in (
        ("source", source_sha),
        ("checkout", checkout_sha),
        ("engine", engine_expected),
        ("engine checkout", engine_actual),
    ):
        sha(value, label)

    if source_sha != checkout_sha or engine_expected != engine_actual:
        raise ValueError("Source or engine checkout is not the exact expected SHA")
    if workflow_run_id <= 0 or run_attempt <= 0:
        raise ValueError("Invalid GitHub workflow run identity")
    if event_name not in {"push", "pull_request", "workflow_dispatch"}:
        raise ValueError("Unexpected canonical workflow event")
    if Path(trx_name).name != trx_name or not trx_name.endswith(".trx"):
        raise ValueError("TRX filename is invalid")

    test_counters = counters_from_trx(trx)
    return {
        "schemaVersion": 1,
        "kind": "arsas-canonical-regression-evidence",
        "canonicalWorkflow": "Build ARSAS",
        "workflowRunId": workflow_run_id,
        "runAttempt": run_attempt,
        "eventName": event_name,
        "exactSourceSha": checkout_sha,
        "exactEngineSha": engine_actual,
        "testFile": trx_name,
        "testTrxSha256": hashlib.sha256(trx).hexdigest(),
        "testCounters": test_counters,
        "fullRegressionPassed": True,
        "releasePromotionAuthority": False,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app-dir", type=Path, required=True)
    parser.add_argument("--engine-dir", type=Path, required=True)
    parser.add_argument("--test-results-dir", type=Path, required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--engine-sha", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    parser.add_argument("--attempt", type=int, required=True)
    parser.add_argument("--event-name", required=True)
    args = parser.parse_args()

    trx_file = args.test_results_dir / "arsas-tests.trx"
    if not trx_file.is_file():
        raise FileNotFoundError(f"Canonical test evidence is missing: {trx_file}")
    blob = trx_file.read_bytes()
    manifest = create_manifest(
        source_sha=sha(args.source_sha, "source"),
        checkout_sha=git_head(args.app_dir),
        engine_expected=sha(args.engine_sha, "expected engine"),
        engine_actual=git_head(args.engine_dir),
        workflow_run_id=args.run_id,
        run_attempt=args.attempt,
        event_name=args.event_name,
        trx=blob,
        trx_name=trx_file.name,
    )
    output = args.test_results_dir / "ci-canonical-authority.json"
    output.write_text(json.dumps(manifest, sort_keys=True, indent=2) + "\n")
    print(
        f"Canonical SHA proof: source={manifest['exactSourceSha']} "
        f"engine={manifest['exactEngineSha']}, "
        f"tests={manifest['testCounters']['passed']} PASS; "
        f"run={manifest['workflowRunId']}/{manifest['runAttempt']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
