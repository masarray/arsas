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
CONSENT_STORAGE_KEY = "arsas_analytics_consent_v1"


def configure(site: Path, measurement_id: str) -> None:
    measurement_id = measurement_id.strip().upper()
    if measurement_id and not MEASUREMENT_PATTERN.fullmatch(measurement_id):
        raise SystemExit("Measurement ID must use the G-XXXXXXXX format")
    if not site.is_dir():
        raise SystemExit(f"Site directory does not exist: {site}")
    for required in ("analytics.js", "consent.js"):
        if not (site / required).is_file():
            raise SystemExit(f"Built site is missing {required}")

    build_info_path = site / "build-info.json"
    if not build_info_path.is_file():
        raise SystemExit("Built site is missing build-info.json")
    build_info = json.loads(build_info_path.read_text(encoding="utf-8"))
    registered = build_info.get("pages")
    if not isinstance(registered, list) or not registered or not all(isinstance(item, str) for item in registered):
        raise SystemExit("build-info.json has an invalid page registry")

    pages = [site / item for item in registered]
    missing = [str(page.relative_to(site)) for page in pages if not page.is_file()]
    if missing:
        raise SystemExit("Registered measurement pages are missing: " + ", ".join(missing))

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

    build_info["measurement"] = {
        "provider": "google-analytics-4",
        "enabled": bool(measurement_id),
        "configElement": "arsas-analytics",
        "client": "analytics.js",
        "consentController": "consent.js",
        "consentRequired": True,
        "defaultConsent": "denied",
        "preferenceStorage": "localStorage",
        "preferenceKey": CONSENT_STORAGE_KEY,
        "doNotTrackRespected": True,
        "advertisingSignals": False,
        "adPersonalizationSignals": False,
    }
    build_info_path.write_text(
        json.dumps(build_info, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    state = "configured behind consent" if measurement_id else "disabled"
    print(f"ARSAS site measurement {state}: {replacements} registered pages configured.")


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
