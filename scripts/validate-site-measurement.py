#!/usr/bin/env python3
"""Validate ARSAS consent-gated measurement and privacy instrumentation."""

from __future__ import annotations

import argparse
import json
import re
import sys
from html.parser import HTMLParser
from pathlib import Path

MEASUREMENT_PATTERN = re.compile(r"G-[A-Z0-9]+")
PLACEHOLDER = "__ARSAS_GA4_MEASUREMENT_ID__"
CONSENT_KEY = "arsas_analytics_consent_v1"


class Parser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.scripts: list[dict[str, str | None]] = []
        self.body_page: str | None = None
        self.privacy_page = False
        self.language: str | None = None
        self.robots: str | None = None
        self.consent_banners = 0
        self.consent_manage = 0
        self.consent_status = 0
        self.alternates: dict[str, str] = {}

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "html":
            self.language = values.get("lang")
        elif tag == "body":
            self.body_page = values.get("data-page")
            self.privacy_page = values.get("data-privacy-page") == "true"
        elif tag == "script":
            self.scripts.append(values)
        elif tag == "meta" and values.get("name", "").lower() == "robots":
            self.robots = values.get("content")
        elif tag == "link" and values.get("rel") == "alternate" and values.get("hreflang"):
            self.alternates[values.get("hreflang") or ""] = values.get("href") or ""
        if "data-consent-banner" in values:
            self.consent_banners += 1
        if "data-consent-manage" in values:
            self.consent_manage += 1
        if "data-consent-status" in values:
            self.consent_status += 1


def parse_page(path: Path) -> tuple[str, Parser]:
    text = path.read_text(encoding="utf-8")
    parsed = Parser()
    parsed.feed(text)
    return text, parsed


def validate_inert_config(config: dict[str, str | None], expected_id: str, label: str, errors: list[str]) -> None:
    if config.get("type") != "application/json" or config.get("src") is not None:
        errors.append(f"{label}: analytics configuration must be inert and local")
    if (config.get("data-measurement-id") or "") != expected_id:
        errors.append(f"{label}: measurement ID does not match deployment configuration")
    if not re.fullmatch(r"\d+\.\d+\.\d+", config.get("data-stable-version") or ""):
        errors.append(f"{label}: stable release context is missing")


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

    analytics_path = site / "analytics.js"
    consent_path = site / "consent.js"
    analytics_text = analytics_path.read_text(encoding="utf-8") if analytics_path.exists() else ""
    consent_text = consent_path.read_text(encoding="utf-8") if consent_path.exists() else ""
    if not analytics_text:
        errors.append("analytics.js is missing")
    if not consent_text:
        errors.append("consent.js is missing")

    for required in (
        "download_installer", "download_portable", "download_checksums",
        "page_not_found", "language_switch", "reportVital('LCP'", "reportVital('CLS'",
        "reportVital('INP'", "allow_google_signals: false", "allow_ad_personalization_signals: false",
    ):
        if required not in analytics_text:
            errors.append(f"analytics.js missing measurement contract: {required}")
    for required in (
        CONSENT_KEY, "analytics_storage: 'denied'", "ad_storage: 'denied'",
        "ad_user_data: 'denied'", "ad_personalization: 'denied'", "navigator.doNotTrack",
        "const privacyPage", "if (privacyPage || analyticsLoaded", "client.src = 'analytics.js'",
        "!privacyPage && (analyticsLoaded || previous === 'granted')",
    ):
        if required not in consent_text:
            errors.append(f"consent.js missing privacy contract: {required}")
    if "googletagmanager.com" in consent_text:
        errors.append("consent.js must load only the local analytics client")

    build_info_path = site / "build-info.json"
    build_info: dict[str, object] = {}
    registered: list[str] = []
    privacy_pages: list[str] = []
    if not build_info_path.exists():
        errors.append("build-info.json is missing")
    else:
        build_info = json.loads(build_info_path.read_text(encoding="utf-8"))
        raw_pages = build_info.get("pages")
        if not isinstance(raw_pages, list) or not raw_pages or not all(isinstance(item, str) for item in raw_pages):
            errors.append("build-info.json has an invalid page registry")
        else:
            registered = list(raw_pages)
        raw_privacy = build_info.get("privacyPages")
        if raw_privacy != ["privacy.html", "privasi.html"]:
            errors.append("build-info.json must declare privacy.html and privasi.html")
        else:
            privacy_pages = list(raw_privacy)

    for relative in registered:
        page = site / relative
        if not page.exists():
            errors.append(f"registered page is missing: {relative}")
            continue
        text, parsed = parse_page(page)
        if PLACEHOLDER in text:
            errors.append(f"{relative}: unresolved measurement placeholder")
        configs = [item for item in parsed.scripts if item.get("id") == "arsas-analytics"]
        if len(configs) != 1:
            errors.append(f"{relative}: expected one inert analytics configuration")
        else:
            validate_inert_config(configs[0], expected_id, relative, errors)
        consent_scripts = [item for item in parsed.scripts if item.get("src") == "consent.js"]
        if len(consent_scripts) != 1 or "defer" not in consent_scripts[0]:
            errors.append(f"{relative}: expected one deferred consent controller")
        if any(item.get("src") == "analytics.js" for item in parsed.scripts):
            errors.append(f"{relative}: analytics.js must not load before consent")
        if parsed.privacy_page:
            errors.append(f"{relative}: registered product page must not be marked as a privacy page")
        if parsed.consent_banners != 1 or parsed.consent_manage < 1 or parsed.consent_status < 1:
            errors.append(f"{relative}: consent banner or preference controls are incomplete")
        if parsed.language not in {"en", "id"}:
            errors.append(f"{relative}: language is unavailable for consent copy")
        if relative == "404.html" and parsed.body_page != "none":
            errors.append("404.html must use data-page=none for page_not_found measurement")

    expected_privacy_alternates = {
        "en": "https://masarray.github.io/arsas/privacy.html",
        "id": "https://masarray.github.io/arsas/privasi.html",
        "x-default": "https://masarray.github.io/arsas/privacy.html",
    }
    for relative in privacy_pages:
        page = site / relative
        if not page.exists():
            errors.append(f"privacy page is missing: {relative}")
            continue
        text, parsed = parse_page(page)
        if PLACEHOLDER in text:
            errors.append(f"{relative}: unresolved measurement placeholder")
        if parsed.robots != "noindex,follow":
            errors.append(f"{relative}: privacy page must use noindex,follow")
        if parsed.alternates != expected_privacy_alternates:
            errors.append(f"{relative}: bilingual privacy alternates are incomplete")
        if not parsed.privacy_page:
            errors.append(f"{relative}: privacy page marker is missing")
        configs = [item for item in parsed.scripts if item.get("id") == "arsas-analytics"]
        if len(configs) != 1:
            errors.append(f"{relative}: privacy page needs one inert availability configuration")
        else:
            validate_inert_config(configs[0], expected_id, relative, errors)
        if any(item.get("src") == "analytics.js" for item in parsed.scripts):
            errors.append(f"{relative}: privacy policy must never load analytics.js")
        consent_scripts = [item for item in parsed.scripts if item.get("src") == "consent.js"]
        if len(consent_scripts) != 1 or "defer" not in consent_scripts[0]:
            errors.append(f"{relative}: privacy page must load the consent controller")
        if parsed.consent_banners != 1 or parsed.consent_manage < 2 or parsed.consent_status < 1:
            errors.append(f"{relative}: privacy preference controls are incomplete")
        if CONSENT_KEY not in text or ("two-month" not in text and "dua bulan" not in text):
            errors.append(f"{relative}: retention or preference-key disclosure is incomplete")

    measurement = build_info.get("measurement")
    if not isinstance(measurement, dict):
        errors.append("build-info.json is missing measurement status")
    else:
        expected = {
            "provider": "google-analytics-4",
            "enabled": bool(expected_id),
            "consentRequired": True,
            "defaultConsent": "denied",
            "preferenceStorage": "localStorage",
            "preferenceKey": CONSENT_KEY,
            "doNotTrackRespected": True,
            "advertisingSignals": False,
            "adPersonalizationSignals": False,
        }
        for key, value in expected.items():
            if measurement.get(key) != value:
                errors.append(f"build-info.json measurement.{key} must be {value!r}")
    privacy = build_info.get("privacy")
    if not isinstance(privacy, dict):
        errors.append("build-info.json privacy contract is missing")
    else:
        expected_privacy = {
            "consentRequired": True,
            "defaultAnalyticsConsent": "denied",
            "measurementAvailable": bool(expected_id),
            "analyticsClientLoadedOnPolicyPages": False,
        }
        for key, value in expected_privacy.items():
            if privacy.get(key) != value:
                errors.append(f"build-info.json privacy.{key} must be {value!r}")

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS consent and measurement validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    state = "configured behind consent" if expected_id else "disabled/no-op"
    print(
        f"ARSAS consent and measurement validation passed: {len(registered)} product pages, "
        f"{len(privacy_pages)} preference-capable non-tracking privacy pages, client {state}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
