#!/usr/bin/env python3
"""Generate bilingual noindex privacy pages with shared ARSAS chrome."""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BUILDER_PATH = ROOT / "scripts" / "build-product-site.py"
SOURCE = ROOT / "landing" / "privacy-source"
MEASUREMENT_PATTERN = re.compile(r"G-[A-Z0-9]+")
PLACEHOLDER = "__ARSAS_GA4_MEASUREMENT_ID__"


def load_builder():
    spec = importlib.util.spec_from_file_location("arsas_product_builder", BUILDER_PATH)
    if spec is None or spec.loader is None:
        raise SystemExit("Cannot load product website builder")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default=str(ROOT / "_site"))
    parser.add_argument("--release-evidence", default=str(ROOT / "landing" / "latest.json"))
    parser.add_argument(
        "--measurement-id",
        default=os.environ.get("GA4_MEASUREMENT_ID", ""),
        help="Public GA4 web-stream ID. It remains inert on privacy pages.",
    )
    args = parser.parse_args()

    measurement_id = args.measurement_id.strip().upper()
    if measurement_id and not MEASUREMENT_PATTERN.fullmatch(measurement_id):
        raise SystemExit("Measurement ID must use the G-XXXXXXXX format")

    output = Path(args.output).resolve()
    evidence_path = Path(args.release_evidence).resolve()
    if not output.is_dir() or not (output / "build-info.json").is_file():
        raise SystemExit("Build the product website before generating privacy pages")

    builder = load_builder()
    config = builder.read_config()
    version = builder.read_version()
    evidence, notes = builder.read_release_data(evidence_path)
    values = builder.token_values(config, version, evidence, notes)
    width, height = builder.icon_dimensions()
    icon_size = f"{width}x{height}"
    root = str(config["product"]["canonicalRoot"])

    pages = (
        (
            SOURCE / "privacy.en.html.tmpl",
            output / "privacy.html",
            {"en": "privacy.html", "id": "privasi.html", "x-default": "privacy.html"},
        ),
        (
            SOURCE / "privacy.id.html.tmpl",
            output / "privasi.html",
            {"en": "privacy.html", "id": "privasi.html", "x-default": "privacy.html"},
        ),
    )

    for source, target, alternates in pages:
        if not source.is_file():
            raise SystemExit(f"Missing privacy source: {source}")
        rendered = builder.render(source.read_text(encoding="utf-8"), values, icon_size)
        rendered = builder.inject_alternate_links(rendered, {"template": source.name, "alternates": alternates}, root)
        placeholder_count = rendered.count(PLACEHOLDER)
        if placeholder_count != 1:
            raise SystemExit(f"{target.name} must contain exactly one inert analytics configuration")
        rendered = rendered.replace(PLACEHOLDER, measurement_id)
        if 'src="analytics.js"' in rendered:
            raise SystemExit(f"Privacy page must not load analytics.js: {target.name}")
        target.write_text(rendered, encoding="utf-8")

    build_info_path = output / "build-info.json"
    build_info = json.loads(build_info_path.read_text(encoding="utf-8"))
    build_info["privacyPages"] = ["privacy.html", "privasi.html"]
    build_info["privacy"] = {
        "indexing": "noindex,follow",
        "consentRequired": True,
        "defaultAnalyticsConsent": "denied",
        "preferenceStorage": "localStorage",
        "preferenceKey": "arsas_analytics_consent_v1",
        "measurementAvailable": bool(measurement_id),
        "analyticsClientLoadedOnPolicyPages": False,
    }
    build_info_path.write_text(json.dumps(build_info, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    shutil.rmtree(output / "privacy-source", ignore_errors=True)
    state = "measurement available" if measurement_id else "measurement disabled"
    print(f"Generated privacy.html and privasi.html with {state}; policy pages never load analytics.js.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
