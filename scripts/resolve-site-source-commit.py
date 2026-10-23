#!/usr/bin/env python3
"""Resolve the newest commit that changes the deployable ARSAS website contract."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

SITE_PATHS = (
    "landing",
    "scripts/validate-product-source.py",
    "scripts/build-product-site.py",
    "scripts/validate-product-build.py",
    "scripts/apply-search-authority.py",
    "scripts/validate-search-authority.py",
    "scripts/build-responsive-media.py",
    "scripts/validate-responsive-media.py",
    "scripts/validate-adoption-proof.py",
    "scripts/verify-adoption-deployment.py",
    "scripts/submit-indexnow.py",
    "scripts/inject-site-measurement.py",
    "scripts/validate-site-measurement.py",
    "scripts/check-site-health.py",
    "scripts/generate-privacy-pages.py",
    "scripts/stamp-site-build.py",
    "scripts/verify-pages-deployment.py",
    "scripts/resolve-site-source-commit.py",
    ".github/workflows/pages.yml",
)


def git(*args: str) -> str:
    completed = subprocess.run(
        ["git", *args],
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
    )
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or f"git {' '.join(args)} failed")
    return completed.stdout.strip()


def write_output(name: str, value: str) -> None:
    target = os.environ.get("GITHUB_OUTPUT", "").strip()
    if not target:
        return
    with open(target, "a", encoding="utf-8") as handle:
        handle.write(f"{name}={value}\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--head", default="HEAD")
    parser.add_argument("--output", default="")
    parser.add_argument("--require-head", action="store_true", help="Require HEAD itself to be the latest website commit")
    args = parser.parse_args()

    head = git("rev-parse", args.head).lower()
    source = git("log", "-1", "--format=%H", head, "--", *SITE_PATHS).lower()
    if not source:
        raise SystemExit("No commit affecting the ARSAS website contract was found")
    ancestor = subprocess.run(["git", "merge-base", "--is-ancestor", source, head], check=False).returncode == 0
    if not ancestor:
        raise SystemExit(f"Resolved website source {source} is not an ancestor of {head}")
    timestamp = git("show", "-s", "--format=%cI", source)
    subject = git("show", "-s", "--format=%s", source)
    head_is_source = head == source
    if args.require_head and not head_is_source:
        raise SystemExit(f"HEAD {head} does not change the deployable website; latest website source is {source}")

    payload = {
        "schemaVersion": 2,
        "resolvedAtUtc": datetime.now(timezone.utc).isoformat(),
        "headCommit": head,
        "siteSourceCommit": source,
        "siteSourceTimestamp": timestamp,
        "siteSourceSubject": subject,
        "headIsSiteSource": head_is_source,
        "trackedPaths": list(SITE_PATHS),
    }
    if args.output:
        output = Path(args.output).resolve()
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    write_output("head_commit", head)
    write_output("site_source_commit", source)
    write_output("site_source_timestamp", timestamp)
    write_output("head_is_site_source", str(head_is_source).lower())
    print(f"ARSAS site source resolved: head={head}, siteSource={source}, headIsSiteSource={head_is_source}.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ARSAS site source resolution failed: {exc}", file=sys.stderr)
        raise
