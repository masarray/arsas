#!/usr/bin/env python3
"""Validate ARSAS P3 onboarding, FAQ, compatibility, demo, issue intake and supply-chain contracts."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LANDING = ROOT / "landing"
TEMPLATES = LANDING / "templates"
PAIR_MAP = {
    "quick-start.html": "panduan-mulai-arsas.html",
    "faq.html": "faq-arsas.html",
    "compatibility.html": "bukti-kompatibilitas.html",
    "demo.html": "demo-arsas.html",
}
ISSUE_FORMS = {
    "device-compatibility.yml", "connection.yml", "reporting.yml", "file-transfer.yml",
    "goose.yml", "installation.yml", "feature-request.yml", "config.yml",
}
STATUSES = {"verified", "conditional", "observed", "not-tested", "known-issue"}


def read(path: Path, errors: list[str]) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as exc:
        errors.append(f"{path.relative_to(ROOT)}: {exc}")
        return ""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--site", default="")
    args = parser.parse_args()
    errors: list[str] = []

    config = json.loads(read(LANDING / "site.json", errors) or "{}")
    pages = config.get("pages", []) if isinstance(config, dict) else []
    entries = {str(item.get("path", "")): item for item in pages if isinstance(item, dict)}
    if len(pages) != 54:
        errors.append(f"landing/site.json must contain 54 pages, found {len(pages)}")
    localized = [item for item in pages if isinstance(item, dict) and item.get("contentType") == "localized"]
    if len(localized) != 17:
        errors.append(f"landing/site.json must contain 17 Indonesian pages, found {len(localized)}")
    for english, indonesian in PAIR_MAP.items():
        expected = {"en": english, "id": indonesian, "x-default": english}
        for path, language in ((english, "en"), (indonesian, "id")):
            entry = entries.get(path)
            if not isinstance(entry, dict) or entry.get("language") != language or entry.get("alternates") != expected:
                errors.append(f"invalid P3 localization contract for {path}")

    quick = read(TEMPLATES / "quick-start.html", errors)
    quick_id = read(TEMPLATES / "panduan-mulai-arsas.html", errors)
    for text, label in ((quick, "quick-start.html"), (quick_id, "panduan-mulai-arsas.html")):
        for value in ("quick-step-number", "TCP port 102", "Npcap", "MMS", "live", "quality", "timestamp", "reporting", "polling", "diagnostic", "control"):
            if value.lower() not in text.lower():
                errors.append(f"{label}: missing onboarding contract {value}")
        if text.count('class="quick-step"') != 7:
            errors.append(f"{label}: expected seven quick-start steps")

    faq = read(TEMPLATES / "faq.html", errors)
    faq_id = read(TEMPLATES / "faq-arsas.html", errors)
    for text, label in ((faq, "faq.html"), (faq_id, "faq-arsas.html")):
        if '"@type":"FAQPage"' not in text.replace(" ", ""):
            errors.append(f"{label}: missing visible FAQPage structured data")
        if text.count('<details class="faq-item">') != 12:
            errors.append(f"{label}: expected twelve visible FAQ items")
        for value in ("SmartScreen", "Npcap", "SBO", "analytics", "Portable"):
            if value.lower() not in text.lower():
                errors.append(f"{label}: missing FAQ topic {value}")

    evidence_path = LANDING / "device-evidence.json"
    try:
        evidence = json.loads(read(evidence_path, errors) or "{}")
    except json.JSONDecodeError as exc:
        errors.append(f"device-evidence.json: {exc}")
        evidence = {}
    if evidence.get("schemaVersion") != 1 or evidence.get("product") != "ARSAS" or evidence.get("namedDeviceCount") != 0:
        errors.append("device-evidence.json identity or named-device boundary is invalid")
    vocabulary = evidence.get("statusVocabulary")
    if not isinstance(vocabulary, dict) or set(vocabulary) != STATUSES:
        errors.append("device-evidence.json status vocabulary is invalid")
    profiles = evidence.get("profiles")
    profile_ids: set[str] = set()
    if not isinstance(profiles, list) or len(profiles) != 2:
        errors.append("device-evidence.json must contain exactly two bounded anonymized profiles")
        profiles = []
    for profile in profiles:
        if not isinstance(profile, dict):
            errors.append("device-evidence.json contains an invalid profile")
            continue
        profile_id = str(profile.get("id", ""))
        profile_ids.add(profile_id)
        services = profile.get("services")
        if not isinstance(services, dict) or not services or not set(services.values()).issubset(STATUSES):
            errors.append(f"{profile_id}: invalid service status")
        links = profile.get("evidenceLinks")
        if not isinstance(links, list) or len(links) < 2 or not all(str(link).startswith("https://github.com/masarray/") for link in links):
            errors.append(f"{profile_id}: public evidence links are incomplete")
        if not isinstance(profile.get("conditions"), list) or len(profile["conditions"]) < 3:
            errors.append(f"{profile_id}: evidence conditions are incomplete")

    compatibility = read(TEMPLATES / "compatibility.html", errors)
    compatibility_id = read(TEMPLATES / "bukti-kompatibilitas.html", errors)
    for text, label in ((compatibility, "compatibility.html"), (compatibility_id, "bukti-kompatibilitas.html")):
        for profile_id in profile_ids:
            if f'data-evidence-profile="{profile_id}"' not in text:
                errors.append(f"{label}: missing evidence profile {profile_id}")
        for status in ("verified", "conditional", "observed"):
            if f'data-status="{status}"' not in text:
                errors.append(f"{label}: missing status {status}")
        lower = text.lower()
        if "device-evidence.json" not in text or not any(term in lower for term in ("conformance", "conformity")):
            errors.append(f"{label}: missing machine-readable or conformance boundary")

    for name in ("guides.html", "panduan.html"):
        text = read(TEMPLATES / name, errors)
        for value in ("data-guide-filter", "data-guide-search", "data-guide-category", "data-guide-card", "guide-filter.js"):
            if value not in text:
                errors.append(f"{name}: missing guide-filter contract {value}")
        if text.count("data-guide-card") != 11:
            errors.append(f"{name}: expected eleven filterable guide cards")

    for name in ("demo.html", "demo-arsas.html"):
        text = read(TEMPLATES / name, errors)
        if text.count("data-demo-step") != 7 or text.count("data-demo-panel") != 7:
            errors.append(f"{name}: expected seven guided demo steps and panels")
        if "demo.js" not in text or text.count("assets/screenshots/") < 9:
            errors.append(f"{name}: guided demo must use the real screenshot set and controller")
        if "time-to-value claim" not in text and "klaim waktu" not in text:
            errors.append(f"{name}: guided demo must reject unsupported timing claims")

    issue_dir = ROOT / ".github" / "ISSUE_TEMPLATE"
    actual_forms = {path.name for path in issue_dir.glob("*.yml")}
    missing_forms = ISSUE_FORMS - actual_forms
    if missing_forms:
        errors.append("missing structured issue forms: " + ", ".join(sorted(missing_forms)))
    for name in ISSUE_FORMS - {"config.yml"}:
        text = read(issue_dir / name, errors)
        for value in ("name:", "description:", "body:", "validations:"):
            if value not in text:
                errors.append(f"{name}: incomplete issue-form contract {value}")
    combined_forms = "\n".join(read(issue_dir / name, errors) for name in ISSUE_FORMS)
    for value in ("ARSAS version", "Windows", "sanitized", "credentials", "confidential"):
        if value.lower() not in combined_forms.lower():
            errors.append(f"issue forms missing intake boundary {value}")

    supply_workflow = read(ROOT / ".github" / "workflows" / "release-supply-chain.yml", errors)
    for value in ("actions/attest@v4", "attestations: write", "id-token: write", "sha256sum --check", "generate-release-sbom.py", "ARSAS-Windows-x64-SBOM.spdx.json"):
        if value not in supply_workflow:
            errors.append(f"release supply-chain workflow missing contract {value}")
    sbom_generator = read(ROOT / "scripts" / "generate-release-sbom.py", errors)
    for value in ("SPDX-2.3", "packageVerificationCode", "SHA256", "GPL-3.0-or-later"):
        if value not in sbom_generator:
            errors.append(f"SBOM generator missing contract {value}")

    for name in ("download.html", "unduh.html", "release-notes.html", "catatan-rilis.html"):
        text = read(TEMPLATES / name, errors)
        lower = text.lower()
        if "SBOM" not in text or "provenance" not in lower:
            errors.append(f"{name}: missing supply-chain evidence guidance")
        if not any(term in lower for term in ("does not claim", "does not infer", "tidak mengklaim", "tidak menganggap")):
            errors.append(f"{name}: missing honest current-release provenance boundary")

    if args.site:
        site = Path(args.site).resolve()
        for path in (*PAIR_MAP.keys(), *PAIR_MAP.values(), "device-evidence.json"):
            if not (site / path).is_file():
                errors.append(f"built site missing P3 output {path}")
        build_info = json.loads(read(site / "build-info.json", errors) or "{}")
        if len(build_info.get("pages", [])) != 54:
            errors.append("built site build-info.json must contain 54 pages")

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS adoption and field-proof validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print("ARSAS adoption and field-proof validation passed: 54 pages, 17 localized pages, onboarding, FAQ, evidence, demo, issue intake, responsive media and future supply-chain attestations.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
