#!/usr/bin/env python3
"""Configure optional site measurement after the deterministic website build."""

from __future__ import annotations

import argparse
import json
import os
import re
from pathlib import Path

PLACEHOLDER = "__ARSAS_GA4_MEASUREMENT_ID__"
MEASUREMENT_PATTERN = re.compile(r"G-[A-Z0-9]+")


def configure(site: Path, measurement_id: str) -> None:
    measurement_id = measurement_id.strip().upper()
    if measurement_id and not MEASUREMENT_PATTERN.fullmatch(measurement_id):
        raise SystemExit("Measurement ID must use the G-XXXXXXXX format")
    if not site.is_dir():
        raise SystemExit(f"Site directory does not exist: {site}")
    if not (site / "analytics.js").is_file():
        raise SystemExit("Built site is missing analytics.js")

    pages = sorted(site.rglob("*.html"))
    if not pages:
        raise SystemExit("Built site contains no HTML pages")

    replacements = 0
    for page in pages:
        text = page.read_text(encoding="utf-8")
        count = text.count(PLACEHOLDER)
        if count != 1:
            raise SystemExit(
                f"{page.relative_to(site)} must contain exactly one measurement placeholder, found {count}"
            )
        page.write_text(text.replace(PLACEHOLDER, measurement_id), encoding="utf-8")
        replacements += count

    build_info_path = site / "build-info.json"
    if build_info_path.exists():
        build_info = json.loads(build_info_path.read_text(encoding="utf-8"))
        build_info["measurement"] = {
            "provider": "google-analytics-4",
            "enabled": bool(measurement_id),
            "client": "analytics.js",
            "doNotTrackRespected": True,
            "advertisingSignals": False,
        }
        build_info_path.write_text(
            json.dumps(build_info, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )

    state = "enabled" if measurement_id else "disabled"
    print(f"ARSAS site measurement {state}: {replacements} pages configured.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("site", nargs="?", default="_site")
    parser.add_argument(
        "--measurement-id",
        default=os.environ.get("GA4_MEASUREMENT_ID", ""),
        help="Public measurement ID. Empty keeps client measurement disabled.",
    )
    args = parser.parse_args()
    configure(Path(args.site).resolve(), args.measurement_id)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
