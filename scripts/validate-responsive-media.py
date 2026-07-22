#!/usr/bin/env python3
"""Validate responsive screenshot variants, rendered srcsets and hero preload candidates."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

IMG_PATTERN = re.compile(r'<img\s+[^>]*src="(assets/screenshots/[^"/]+\.webp)"[^>]*>', re.IGNORECASE)
PRELOAD_PATTERN = re.compile(
    r'<link\s+[^>]*rel="preload"[^>]*as="image"[^>]*href="(assets/screenshots/[^"/]+\.webp)"[^>]*>',
    re.IGNORECASE,
)
SRCSET_PATTERN = re.compile(r'\bsrcset="([^"]+)"', re.IGNORECASE)
IMAGE_SRCSET_PATTERN = re.compile(r'\bimagesrcset="([^"]+)"', re.IGNORECASE)


def validate_candidates(site: Path, page: str, value: str, label: str, errors: list[str]) -> None:
    candidates = [item.strip().split(" ", 1)[0] for item in value.split(",")]
    if len(candidates) < 2:
        errors.append(f"{page}: {label} has fewer than two candidates")
    for candidate in candidates:
        if not (site / candidate).is_file():
            errors.append(f"{page}: responsive candidate is missing: {candidate}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("site", nargs="?", default="_site")
    args = parser.parse_args()
    site = Path(args.site).resolve()
    errors: list[str] = []
    image_count = preload_count = page_count = 0

    for page in sorted(site.glob("*.html")):
        html = page.read_text(encoding="utf-8")
        page_instrumented = False
        for match in IMG_PATTERN.finditer(html):
            tag = match.group(0)
            page_instrumented = True
            image_count += 1
            if 'data-responsive-media="webp"' not in tag or 'sizes="' not in tag:
                errors.append(f"{page.name}: screenshot lacks responsive marker or sizes: {match.group(1)}")
                continue
            srcset_match = SRCSET_PATTERN.search(tag)
            if not srcset_match:
                errors.append(f"{page.name}: screenshot lacks srcset: {match.group(1)}")
                continue
            validate_candidates(site, page.name, srcset_match.group(1), "screenshot srcset", errors)

        for match in PRELOAD_PATTERN.finditer(html):
            tag = match.group(0)
            page_instrumented = True
            preload_count += 1
            if 'data-responsive-media-preload="webp"' not in tag or 'imagesizes="' not in tag:
                errors.append(f"{page.name}: image preload lacks responsive marker or imagesizes: {match.group(1)}")
                continue
            srcset_match = IMAGE_SRCSET_PATTERN.search(tag)
            if not srcset_match:
                errors.append(f"{page.name}: image preload lacks imagesrcset: {match.group(1)}")
                continue
            validate_candidates(site, page.name, srcset_match.group(1), "preload imagesrcset", errors)

        if page_instrumented:
            page_count += 1

    try:
        info = json.loads((site / "build-info.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"build-info.json: {exc}")
        info = {}
    responsive = info.get("responsiveMedia")
    if not isinstance(responsive, dict):
        errors.append("build-info.json is missing responsiveMedia evidence")
    else:
        if responsive.get("schemaVersion") != 2 or responsive.get("format") != "webp":
            errors.append("responsiveMedia identity is invalid")
        expected = {
            "instrumentedImages": image_count,
            "instrumentedPreloads": preload_count,
            "instrumentedPages": page_count,
        }
        for key, value in expected.items():
            if responsive.get(key) != value:
                errors.append(f"responsiveMedia {key} is {responsive.get(key)!r}, expected {value}")
        if int(responsive.get("variantCount", 0) or 0) < int(responsive.get("sourceCount", 0) or 0) * 2:
            errors.append("responsiveMedia has too few generated variants")

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS responsive-media validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print(
        f"ARSAS responsive-media validation passed: {image_count} images, {preload_count} responsive preloads "
        f"on {page_count} pages."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
