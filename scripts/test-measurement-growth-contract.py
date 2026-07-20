#!/usr/bin/env python3
"""Exercise ARSAS live measurement validation and measured growth queue branches offline."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def run(command: list[str], expected: int = 0) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        command,
        cwd=ROOT,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        check=False,
    )
    if result.returncode != expected:
        raise RuntimeError(
            f"Command returned {result.returncode}, expected {expected}: {' '.join(command)}\n"
            f"stdout:\n{result.stdout}\nstderr:\n{result.stderr}"
        )
    return result


def write(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--site", default="_site")
    args = parser.parse_args()
    site = Path(args.site).resolve()
    if not (site / "site.json").is_file():
        raise SystemExit(f"Built site registry is missing: {site / 'site.json'}")

    with tempfile.TemporaryDirectory(prefix="arsas-measurement-contract-") as raw:
        temp = Path(raw)
        readiness_path = temp / "readiness.json"
        healthy_path = temp / "healthy.json"
        warning_path = temp / "warning.json"
        measured_path = temp / "measured.json"
        output = temp / "growth"

        readiness = {
            "schemaVersion": 1,
            "state": "fully-ready",
            "authenticationMode": "fixture",
            "checks": {
                "clientCollectionReady": True,
                "ga4ReportingReady": True,
                "searchConsoleReady": True,
                "liveReportingReady": True,
                "fullyReady": True,
            },
            "warnings": [],
            "errors": [],
        }
        healthy = {
            "schemaVersion": 2,
            "authenticationMode": "fixture",
            "ga4": {"status": "available-no-data", "topPages": [], "languageTraffic": {}, "downloadTotals": {}, "notFound": []},
            "searchConsole": {
                "status": "available-no-data",
                "topQueries": [],
                "topPages": [],
                "lowCtrQueries": [],
                "lowCtrPages": [],
                "languageImpressions": {},
                "languageClicks": {},
                "sitemaps": {
                    "status": "healthy",
                    "expectedPath": "https://masarray.github.io/arsas/sitemap.xml",
                    "expected": {"path": "https://masarray.github.io/arsas/sitemap.xml", "warnings": 0, "errors": 0, "isPending": False},
                },
            },
            "coreWebVitals": {"status": "unavailable", "pages": []},
        }
        warning = json.loads(json.dumps(healthy))
        warning["searchConsole"]["sitemaps"]["status"] = "warning"
        warning["searchConsole"]["sitemaps"]["expected"]["warnings"] = 1

        measured = {
            "schemaVersion": 2,
            "authenticationMode": "fixture",
            "ga4": {
                "status": "available",
                "topPages": [],
                "languageTraffic": {"en": 40},
                "downloadTotals": {},
                "notFound": [{"pagePath": "/arsas/missing.html", "pageReferrer": "https://example.test/", "eventCount": 7}],
                "trend": {"pageViews": {"current": 40, "previous": 100, "absolute": -60, "percent": -0.6}},
            },
            "searchConsole": {
                "status": "available",
                "topQueries": [],
                "topPages": [],
                "lowCtrQueries": [{"query": "iec 61850 testing", "clicks": 1, "impressions": 100, "ctr": 0.01, "position": 8}],
                "lowCtrPages": [{"page": "https://masarray.github.io/arsas/smart-reporting.html", "clicks": 2, "impressions": 200, "ctr": 0.01, "position": 7}],
                "languageImpressions": {"en": 180, "id": 20},
                "languageClicks": {"en": 2, "id": 0},
                "trend": {
                    "clicks": {"current": 20, "previous": 50, "absolute": -30, "percent": -0.6},
                    "impressions": {"current": 700, "previous": 1200, "absolute": -500, "percent": -0.4167},
                },
                "sitemaps": {
                    "status": "error",
                    "expectedPath": "https://masarray.github.io/arsas/sitemap.xml",
                    "expected": {"path": "https://masarray.github.io/arsas/sitemap.xml", "warnings": 0, "errors": 2, "isPending": False},
                },
            },
            "coreWebVitals": {
                "status": "available",
                "pages": [{
                    "url": "https://masarray.github.io/arsas/",
                    "status": "available",
                    "fieldCategory": "poor",
                    "field": {},
                    "lab": {"performanceScore": 0.62},
                }],
            },
        }

        write(readiness_path, readiness)
        write(healthy_path, healthy)
        write(warning_path, warning)
        write(measured_path, measured)

        validator = str(ROOT / "scripts" / "validate-measurement-report.py")
        run([sys.executable, validator, "--report", str(healthy_path), "--readiness", str(readiness_path)])
        run([sys.executable, validator, "--report", str(warning_path), "--readiness", str(readiness_path)], expected=1)

        growth = str(ROOT / "scripts" / "build-search-growth-report.py")
        run([
            sys.executable, growth,
            "--site", str(site),
            "--measurement-json", str(measured_path),
            "--output", str(output),
        ])
        payload = json.loads((output / "search-growth.json").read_text(encoding="utf-8"))
        queue = payload.get("queue", [])
        categories = {item.get("category") for item in queue if isinstance(item, dict)}
        required = {"sitemap-health", "search-click-decline", "search-decline", "observed-404", "core-web-vitals"}
        missing = sorted(required - categories)
        if missing:
            raise RuntimeError("Measured growth fixture did not produce categories: " + ", ".join(missing))
        if not queue or queue[0].get("category") != "sitemap-health" or queue[0].get("priority") != 98:
            raise RuntimeError("Sitemap error must be the highest-priority measured growth action")
        if payload.get("schemaVersion") != 2 or payload.get("dataCoverage", {}).get("sitemap") != "error":
            raise RuntimeError("Measured growth output schema or sitemap coverage is invalid")

    print("ARSAS measurement and growth fixture contract passed.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ARSAS measurement and growth fixture contract failed: {exc}", file=sys.stderr)
        raise
