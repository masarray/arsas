#!/usr/bin/env python3
"""Generate bilingual noindex privacy pages with shared ARSAS chrome."""

from __future__ import annotations

import argparse
import importlib.util
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BUILDER_PATH = ROOT / "scripts" / "build-product-site.py"
SOURCE = ROOT / "landing" / "privacy-source"
ANALYTICS_SCRIPT = re.compile(
    r'\s*<script\s+id="arsas-analytics"[^>]*>\s*</script>',
    re.IGNORECASE,
)


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
    args = parser.parse_args()

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
        rendered = ANALYTICS_SCRIPT.sub("", rendered)
        if "__ARSAS_GA4_MEASUREMENT_ID__" in rendered:
            raise SystemExit(f"Privacy page still contains a measurement placeholder: {target.name}")
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
    }
    build_info_path.write_text(json.dumps(build_info, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    shutil.rmtree(output / "privacy-source", ignore_errors=True)
    print("Generated privacy.html and privasi.html with noindex, consent controls and shared ARSAS chrome.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
