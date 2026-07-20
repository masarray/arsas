#!/usr/bin/env python3
"""Validate ARSAS client measurement instrumentation in a rendered site."""

from __future__ import annotations

import argparse
import json
import re
import sys
from html.parser import HTMLParser
from pathlib import Path

MEASUREMENT_PATTERN = re.compile(r"G-[A-Z0-9]+")
PLACEHOLDER = "__ARSAS_GA4_MEASUREMENT_ID__"


class Parser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.analytics: list[dict[str, str | None]] = []
        self.body_page: str | None = None
        self.language: str | None = None

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "html":
            self.language = values.get("lang")
        elif tag == "body":
            self.body_page = values.get("data-page")
        elif tag == "script" and values.get("id") == "arsas-analytics":
            self.analytics.append(values)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("site", nargs="?", default="_site")
    parser.add_argument("--measurement-id", default="")
    args = parser.parse_args()

    site = Path(args.site).resolve()
    expected_id = args.measurement_id.strip().upper()
    errors: list[str] = []
    if expected_id and not MEASUREMENT_PATTERN.fullmatch(expected_id):
        errors.append("expected measurement ID is invalid")

    client = site / "analytics.js"
    if not client.exists():
        errors.append("analytics.js is missing")
        client_text = ""
    else:
        client_text = client.read_text(encoding="utf-8")
    for required in (
        "download_installer", "download_portable", "download_checksums",
        "page_not_found", "language_switch", "web_vital_lcp", "web_vital_cls",
        "web_vital_inp", "navigator.doNotTrack", "allow_google_signals: false",
        "allow_ad_personalization_signals: false",
    ):
        if required not in client_text:
            errors.append(f"analytics.js missing measurement contract: {required}")

    pages = sorted(site.rglob("*.html"))
    if not pages:
        errors.append("rendered site has no HTML pages")
    for page in pages:
        text = page.read_text(encoding="utf-8")
        label = page.relative_to(site)
        if PLACEHOLDER in text:
            errors.append(f"{label}: unresolved measurement placeholder")
        parsed = Parser()
        parsed.feed(text)
        if len(parsed.analytics) != 1:
            errors.append(f"{label}: expected one shared analytics client")
            continue
        script = parsed.analytics[0]
        if script.get("src") != "analytics.js" or script.get("defer") is None:
            errors.append(f"{label}: analytics client must be local and deferred")
        actual_id = script.get("data-measurement-id") or ""
        if actual_id != expected_id:
            errors.append(f"{label}: measurement ID does not match configured deployment value")
        stable_version = script.get("data-stable-version") or ""
        if not re.fullmatch(r"\d+\.\d+\.\d+", stable_version):
            errors.append(f"{label}: stable release version is missing from measurement context")
        if parsed.language not in {"en", "id"}:
            errors.append(f"{label}: language is unavailable for traffic segmentation")
        if page.name == "404.html" and parsed.body_page != "none":
            errors.append("404.html must use data-page=none for page_not_found measurement")

    build_info_path = site / "build-info.json"
    if not build_info_path.exists():
        errors.append("build-info.json is missing")
    else:
        build_info = json.loads(build_info_path.read_text(encoding="utf-8"))
        measurement = build_info.get("measurement")
        if not isinstance(measurement, dict):
            errors.append("build-info.json is missing measurement status")
        else:
            if measurement.get("provider") != "google-analytics-4":
                errors.append("build-info.json has invalid measurement provider")
            if measurement.get("enabled") is not bool(expected_id):
                errors.append("build-info.json measurement enabled state is incorrect")
            if measurement.get("doNotTrackRespected") is not True:
                errors.append("build-info.json must declare Do Not Track handling")
            if measurement.get("advertisingSignals") is not False:
                errors.append("build-info.json must declare advertising signals disabled")

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS site-measurement validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    state = "enabled" if expected_id else "disabled/no-op"
    print(f"ARSAS site-measurement validation passed: {len(pages)} pages, client {state}, downloads, language, 404 and Core Web Vitals contracts present.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
