#!/usr/bin/env python3
"""Write a fail-closed authority manifest for an installed-smoke-tested Windows installer."""
from __future__ import annotations

import argparse, hashlib, json, re
from pathlib import Path

SHA_RE = re.compile(r"^[0-9a-f]{40}$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


def sha(value: str, label: str) -> str:
    value = (value or "").strip().lower()
    if not SHA_RE.fullmatch(value):
        raise ValueError(f"{label} must be a lowercase 40-hex SHA")
    return value


def file_sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--installer", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--version", required=True)
    p.add_argument("--source-sha", required=True)
    p.add_argument("--engine-sha", required=True)
    p.add_argument("--ardirec-sha", required=True)
    p.add_argument("--package-run", required=True)
    p.add_argument("--package-artifact-sha256", required=True)
    p.add_argument("--workflow-run-id", type=int, required=True)
    p.add_argument("--run-attempt", type=int, required=True)
    p.add_argument("--event-name", required=True)
    args = p.parse_args()

    if not args.installer.is_file():
        raise FileNotFoundError(args.installer)
    if args.workflow_run_id <= 0 or args.run_attempt <= 0:
        raise ValueError("Invalid installer workflow identity")
    if args.event_name not in {"push", "pull_request", "workflow_dispatch"}:
        raise ValueError("Unexpected installer workflow event")
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:[-.][0-9A-Za-z.-]+)?", args.version):
        raise ValueError("Invalid installer version")

    package_digest = args.package_artifact_sha256.strip().lower()
    if args.event_name == "workflow_dispatch":
        if package_digest != "manual-local-build":
            raise ValueError("Manual installer must record manual-local-build package authority")
    elif not SHA256_RE.fullmatch(package_digest):
        raise ValueError("Canonical package artifact SHA-256 is invalid")

    size = args.installer.stat().st_size
    if size <= 0:
        raise ValueError("Installer is empty")

    payload = {
        "schemaVersion": 1,
        "kind": "arsas-validated-windows-installer",
        "version": args.version,
        "sourceSha": sha(args.source_sha, "source"),
        "engineSha": sha(args.engine_sha, "engine"),
        "ardirecSha": sha(args.ardirec_sha, "ArdIrec"),
        "canonicalPackageRun": args.package_run,
        "canonicalPackageArtifactSha256": package_digest,
        "installerWorkflow": "Validate ARSAS Windows installer",
        "workflowRunId": args.workflow_run_id,
        "runAttempt": args.run_attempt,
        "eventName": args.event_name,
        "installerFile": args.installer.name,
        "installerSha256": file_sha256(args.installer),
        "installerSizeBytes": size,
        "installedSmokePassed": True,
        "releasePromotionAuthority": False,
    }
    args.output.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(payload, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
