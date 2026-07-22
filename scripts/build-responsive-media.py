#!/usr/bin/env python3
"""Generate responsive WebP screenshot variants and inject srcset metadata into built ARSAS pages."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

from PIL import Image

SCREENSHOT_PATTERN = re.compile(r'<img\s+([^>]*?)src="(assets/screenshots/([^"/]+\.webp))"([^>]*)>', re.IGNORECASE)
WIDTHS = (480, 768, 960, 1280)


def generate(source: Path, target_dir: Path) -> list[tuple[int, Path]]:
    variants: list[tuple[int, Path]] = []
    with Image.open(source) as image:
        original_width, original_height = image.size
        for width in WIDTHS:
            if width >= original_width:
                continue
            height = max(1, round(original_height * width / original_width))
            target = target_dir / f"{source.stem}-{width}.webp"
            target.parent.mkdir(parents=True, exist_ok=True)
            resized = image.resize((width, height), Image.Resampling.LANCZOS)
            resized.save(target, format="WEBP", quality=82, method=6)
            variants.append((width, target))
    return variants


def inject_page(path: Path, variant_map: dict[str, list[tuple[int, Path]]], site: Path) -> int:
    html = path.read_text(encoding="utf-8")
    changed = 0

    def replace(match: re.Match[str]) -> str:
        nonlocal changed
        before, source, filename, after = match.groups()
        if "data-responsive-media=" in before + after or filename not in variant_map:
            return match.group(0)
        candidates = [
            f"{variant.relative_to(site).as_posix()} {width}w"
            for width, variant in variant_map[filename]
        ]
        original_width_match = re.search(r'\bwidth="(\d+)"', before + after)
        if original_width_match:
            candidates.append(f"{source} {original_width_match.group(1)}w")
        if len(candidates) < 2:
            return match.group(0)
        changed += 1
        return (
            f'<img {before}src="{source}" srcset="{", ".join(candidates)}" '
            f'sizes="(max-width: 720px) calc(100vw - 2rem), (max-width: 1180px) calc(100vw - 4rem), 1180px" '
            f'data-responsive-media="webp"{after}>'
        )

    rendered = SCREENSHOT_PATTERN.sub(replace, html)
    if changed:
        path.write_text(rendered, encoding="utf-8")
    return changed


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("site", nargs="?", default="_site")
    args = parser.parse_args()
    site = Path(args.site).resolve()
    screenshot_dir = site / "assets" / "screenshots"
    if not screenshot_dir.is_dir():
        raise SystemExit("Built screenshot directory is missing")

    responsive_dir = screenshot_dir / "responsive"
    variant_map: dict[str, list[tuple[int, Path]]] = {}
    for source in sorted(screenshot_dir.glob("*.webp")):
        variants = generate(source, responsive_dir)
        if variants:
            variant_map[source.name] = variants

    page_count = image_count = 0
    for page in sorted(site.glob("*.html")):
        changed = inject_page(page, variant_map, site)
        if changed:
            page_count += 1
            image_count += changed

    info_path = site / "build-info.json"
    info = json.loads(info_path.read_text(encoding="utf-8"))
    info["responsiveMedia"] = {
        "schemaVersion": 1,
        "format": "webp",
        "sourceCount": len(variant_map),
        "variantCount": sum(len(items) for items in variant_map.values()),
        "instrumentedPages": page_count,
        "instrumentedImages": image_count,
        "targetWidths": list(WIDTHS),
    }
    info_path.write_text(json.dumps(info, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(
        f"Generated {sum(len(items) for items in variant_map.values())} responsive WebP variants "
        f"for {len(variant_map)} screenshots and instrumented {image_count} images on {page_count} pages."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
