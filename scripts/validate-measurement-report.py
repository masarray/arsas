#!/usr/bin/env python3
"""Validate that configured ARSAS measurement sources produced usable private evidence."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

AVAILABLE = {"available", "available-no-data"}


def load(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise SystemExit(f"Cannot read {label}: {exc}") from exc
    if not isinstance(value, dict):
        raise SystemExit(f"{label} must contain a JSON object")
    return value


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True)
    parser.add_argument("--readiness", required=True)
    args = parser.parse_args()

    report = load(Path(args.report).resolve(), "measurement report")
    readiness = load(Path(args.readiness).resolve(), "measurement readiness")
    errors: list[str] = []

    if report.get("schemaVersion") != 2:
        errors.append("measurement report schemaVersion must be 2")
    checks = readiness.get("checks")
    if not isinstance(checks, dict):
        errors.append("readiness checks are missing")
        checks = {}

    ga4 = report.get("ga4") if isinstance(report.get("ga4"), dict) else {}
    search = report.get("searchConsole") if isinstance(report.get("searchConsole"), dict) else {}
    speed = report.get("coreWebVitals") if isinstance(report.get("coreWebVitals"), dict) else {}

    if checks.get("ga4ReportingReady") is True and ga4.get("status") not in AVAILABLE:
        errors.append(f"GA4 was configured as ready but report status is {ga4.get('status')!r}")
    if checks.get("searchConsoleReady") is True:
        if search.get("status") not in AVAILABLE:
            errors.append(f"Search Console was configured as ready but report status is {search.get('status')!r}")
        sitemaps = search.get("sitemaps") if isinstance(search.get("sitemaps"), dict) else {}
        if sitemaps.get("status") not in {"healthy", "pending"}:
            errors.append(f"submitted sitemap is not healthy or pending: {sitemaps.get('status')!r}")
        if sitemaps.get("expectedPath") != "https://masarray.github.io/arsas/sitemap.xml":
            errors.append("Search Console report did not inspect the canonical ARSAS sitemap")

    if checks.get("liveReportingReady") is True and report.get("authenticationMode") == "not-configured":
        errors.append("live reporting was ready but the measurement report used no Google authentication")
    if speed.get("status") not in {"available", "unavailable"}:
        errors.append(f"Core Web Vitals status is invalid: {speed.get('status')!r}")

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS live measurement validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print(
        "ARSAS live measurement validation passed: "
        f"GA4={ga4.get('status')}, Search Console={search.get('status')}, "
        f"sitemap={search.get('sitemaps', {}).get('status') if isinstance(search.get('sitemaps'), dict) else 'unavailable'}, "
        f"Core Web Vitals={speed.get('status')}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
