#!/usr/bin/env python3
"""Validate the final GitHub Pages artifact after media localization."""

from __future__ import annotations

import struct
import sys
from html.parser import HTMLParser
from pathlib import Path

EXPECTED_PAGES = (
    "index.html",
    "download.html",
    "smart-reporting.html",
    "features.html",
    "control.html",
    "architecture.html",
    "roadmap.html",
    "404.html",
)

EXPECTED_MEDIA = (
    "assets/social-card.png",
    "assets/screenshots/arsas-first-launch.webp",
    "assets/screenshots/arsas-multi-ied.webp",
    "assets/screenshots/arsas-live-values.webp",
    "assets/screenshots/arsas-event-log.webp",
    "assets/screenshots/arsas-goose.webp",
    "assets/screenshots/arsas-diagnostics.webp",
)


class AssetParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.refs: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        for key in ("href", "src", "content"):
            value = values.get(key)
            if value:
                self.refs.append(value)


def png_size(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG")
    return struct.unpack(">II", data[16:24])


def main() -> int:
    site = Path(sys.argv[1] if len(sys.argv) > 1 else "_site").resolve()
    errors: list[str] = []

    for relative in EXPECTED_PAGES + EXPECTED_MEDIA:
        if not (site / relative).exists():
            errors.append(f"missing deployable file: {relative}")

    combined = ""
    for page_name in EXPECTED_PAGES:
        page = site / page_name
        if not page.exists():
            continue
        text = page.read_text(encoding="utf-8")
        combined += text
        parser = AssetParser()
        parser.feed(text)
        for ref in parser.refs:
            if ref.startswith("assets/") and not (site / ref.split("?", 1)[0].split("#", 1)[0]).exists():
                errors.append(f"{page_name}: missing local asset {ref}")

    forbidden = (
        "raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot",
        "https://masarray.github.io/arsas/assets/social-card.svg",
    )
    for value in forbidden:
        if value in combined:
            errors.append(f"deployable HTML still references {value}")

    if "https://masarray.github.io/arsas/assets/social-card.png" not in combined:
        errors.append("PNG social preview is not referenced")
    if 'href="download.html"' not in combined:
        errors.append("stable download page is not used by landing CTAs")

    download = site / "download.html"
    if download.exists():
        text = download.read_text(encoding="utf-8")
        for required in ("data-installer-link", "data-portable-link", "download.js", "releases/latest"):
            if required not in text and required not in (site / "download.js").read_text(encoding="utf-8"):
                errors.append(f"download workflow is missing {required}")

    social = site / "assets/social-card.png"
    if social.exists():
        try:
            if png_size(social) != (1200, 630):
                errors.append(f"social-card.png must be 1200x630, found {png_size(social)}")
        except ValueError as exc:
            errors.append(f"social-card.png: {exc}")

    if errors:
        print("Deployable landing validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Deployable landing validation passed: local screenshots, PNG social preview, and release-aware download workflow are consistent.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
