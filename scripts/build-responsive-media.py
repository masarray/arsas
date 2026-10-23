#!/usr/bin/env python3
"""Generate responsive WebP variants and inject image plus preload metadata into built ARSAS pages."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any

from PIL import Image

SCREENSHOT_PATTERN = re.compile(r'<img\s+([^>]*?)src="(assets/screenshots/([^"/]+\.webp))"([^>]*)>', re.IGNORECASE)
PRELOAD_PATTERN = re.compile(
    r'<link\s+([^>]*?)rel="preload"([^>]*?)as="image"([^>]*?)href="(assets/screenshots/([^"/]+\.webp))"([^>]*)>',
    re.IGNORECASE,
)
WIDTHS = (480, 768, 960, 1280)
SIZES = "(max-width: 720px) calc(100vw - 2rem), (max-width: 1180px) calc(100vw - 4rem), 1180px"


def generate(source: Path, target_dir: Path) -> dict[str, Any]:
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
    return {"originalWidth": original_width, "variants": variants}


def candidates(source: str, media: dict[str, Any], site: Path) -> str:
    values = [
        f"{variant.relative_to(site).as_posix()} {width}w"
        for width, variant in media["variants"]
    ]
    values.append(f"{source} {media['originalWidth']}w")
    return ", ".join(values)


def inject_page(path: Path, media_map: dict[str, dict[str, Any]], site: Path) -> tuple[int, int]:
    html = path.read_text(encoding="utf-8")
    image_changes = preload_changes = 0

    def replace_image(match: re.Match[str]) -> str:
        nonlocal image_changes
        before, source, filename, after = match.groups()
        if "data-responsive-media=" in before + after or filename not in media_map:
            return match.group(0)
        srcset = candidates(source, media_map[filename], site)
        if srcset.count(",") < 1:
            return match.group(0)
        image_changes += 1
        return (
            f'<img {before}src="{source}" srcset="{srcset}" sizes="{SIZES}" '
            f'data-responsive-media="webp"{after}>'
        )

    def replace_preload(match: re.Match[str]) -> str:
        nonlocal preload_changes
        before, middle_one, middle_two, source, filename, after = match.groups()
        full = match.group(0)
        if "data-responsive-media-preload=" in full or filename not in media_map:
            return full
        srcset = candidates(source, media_map[filename], site)
        preload_changes += 1
        return (
            f'<link {before}rel="preload"{middle_one}as="image"{middle_two}href="{source}" '
            f'imagesrcset="{srcset}" imagesizes="{SIZES}" data-responsive-media-preload="webp"{after}>'
        )

    rendered = SCREENSHOT_PATTERN.sub(replace_image, html)
    rendered = PRELOAD_PATTERN.sub(replace_preload, rendered)
    if image_changes or preload_changes:
        path.write_text(rendered, encoding="utf-8")
    return image_changes, preload_changes


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("site", nargs="?", default="_site")
    args = parser.parse_args()
    site = Path(args.site).resolve()
    screenshot_dir = site / "assets" / "screenshots"
    if not screenshot_dir.is_dir():
        raise SystemExit("Built screenshot directory is missing")

    responsive_dir = screenshot_dir / "responsive"
    media_map: dict[str, dict[str, Any]] = {}
    for source in sorted(screenshot_dir.glob("*.webp")):
        media = generate(source, responsive_dir)
        if media["variants"]:
            media_map[source.name] = media

    page_count = image_count = preload_count = 0
    for page in sorted(site.glob("*.html")):
        images, preloads = inject_page(page, media_map, site)
        if images or preloads:
            page_count += 1
            image_count += images
            preload_count += preloads

    info_path = site / "build-info.json"
    info = json.loads(info_path.read_text(encoding="utf-8"))
    info["responsiveMedia"] = {
        "schemaVersion": 2,
        "format": "webp",
        "sourceCount": len(media_map),
        "variantCount": sum(len(item["variants"]) for item in media_map.values()),
        "instrumentedPages": page_count,
        "instrumentedImages": image_count,
        "instrumentedPreloads": preload_count,
        "targetWidths": list(WIDTHS),
    }
    info_path.write_text(json.dumps(info, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(
        f"Generated {sum(len(item['variants']) for item in media_map.values())} responsive WebP variants "
        f"for {len(media_map)} screenshots; instrumented {image_count} images and {preload_count} preloads "
        f"on {page_count} pages."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
