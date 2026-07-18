#!/usr/bin/env python3
"""Validate the ARSAS static product website without external dependencies."""

from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[1]
SITE = ROOT / "landing"
CANONICAL_ROOT = "https://masarray.github.io/arsas/"
HTML_FILES = [
    SITE / "index.html",
    SITE / "smart-reporting.html",
    SITE / "features.html",
    SITE / "control.html",
    SITE / "architecture.html",
    SITE / "roadmap.html",
    SITE / "404.html",
]


class PageParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.title_parts: list[str] = []
        self.in_title = False
        self.h1_count = 0
        self.description: str | None = None
        self.canonical: str | None = None
        self.links: list[str] = []
        self.images: list[tuple[str, str | None]] = []
        self.scripts: list[str] = []
        self.stylesheets: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "title":
            self.in_title = True
        elif tag == "h1":
            self.h1_count += 1
        elif tag == "meta" and values.get("name", "").lower() == "description":
            self.description = values.get("content")
        elif tag == "link":
            rel = (values.get("rel") or "").lower()
            href = values.get("href")
            if rel == "canonical":
                self.canonical = href
            if "stylesheet" in rel and href:
                self.stylesheets.append(href)
        elif tag == "a" and values.get("href"):
            self.links.append(values["href"] or "")
        elif tag == "img" and values.get("src"):
            self.images.append((values["src"] or "", values.get("alt")))
        elif tag == "script" and values.get("src"):
            self.scripts.append(values["src"] or "")

    def handle_endtag(self, tag: str) -> None:
        if tag == "title":
            self.in_title = False

    def handle_data(self, data: str) -> None:
        if self.in_title:
            self.title_parts.append(data)


def is_external(reference: str) -> bool:
    parsed = urlparse(reference)
    return bool(parsed.scheme or parsed.netloc or reference.startswith("mailto:") or reference.startswith("#"))


def validate_local_reference(page: Path, reference: str, errors: list[str]) -> None:
    clean = reference.split("#", 1)[0].split("?", 1)[0]
    if not clean or is_external(reference) or clean.startswith("data:"):
        return
    target = (page.parent / clean).resolve()
    try:
        target.relative_to(SITE.resolve())
    except ValueError:
        errors.append(f"{page.relative_to(ROOT)}: local reference escapes landing directory: {reference}")
        return
    if not target.exists():
        errors.append(f"{page.relative_to(ROOT)}: missing local reference: {reference}")


def validate_html(page: Path, errors: list[str]) -> None:
    if not page.exists():
        errors.append(f"Missing page: {page.relative_to(ROOT)}")
        return

    text = page.read_text(encoding="utf-8")
    parser = PageParser()
    parser.feed(text)

    title = "".join(parser.title_parts).strip()
    if not title:
        errors.append(f"{page.relative_to(ROOT)}: missing title")
    elif len(title) > 75:
        errors.append(f"{page.relative_to(ROOT)}: title is longer than 75 characters ({len(title)})")

    if page.name != "404.html":
        if parser.h1_count != 1:
            errors.append(f"{page.relative_to(ROOT)}: expected exactly one h1, found {parser.h1_count}")
        if not parser.description:
            errors.append(f"{page.relative_to(ROOT)}: missing meta description")
        elif not 70 <= len(parser.description) <= 220:
            errors.append(
                f"{page.relative_to(ROOT)}: meta description length should be 70-220 characters, found {len(parser.description)}"
            )
        if not parser.canonical or not parser.canonical.startswith(CANONICAL_ROOT):
            errors.append(f"{page.relative_to(ROOT)}: missing or invalid canonical URL")

    for reference in parser.links + parser.scripts + parser.stylesheets:
        validate_local_reference(page, reference, errors)

    for src, alt in parser.images:
        validate_local_reference(page, src, errors)
        if alt is None:
            errors.append(f"{page.relative_to(ROOT)}: image is missing alt text: {src}")

    if "http://" in text:
        errors.append(f"{page.relative_to(ROOT)}: insecure http:// reference")
    if "ArIED61850Tester" in text or "ArIED 61850" in text:
        errors.append(f"{page.relative_to(ROOT)}: contains legacy product or repository branding")


def validate_structured_files(errors: list[str]) -> None:
    manifest_path = SITE / "site.webmanifest"
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        for required in ("name", "short_name", "description", "start_url", "display", "theme_color"):
            if not manifest.get(required):
                errors.append(f"landing/site.webmanifest: missing {required}")
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"landing/site.webmanifest: {exc}")

    for relative in ("sitemap.xml", "assets/hero.svg", "assets/social-card.svg", "assets/favicon.svg"):
        path = SITE / relative
        try:
            ET.parse(path)
        except (OSError, ET.ParseError) as exc:
            errors.append(f"landing/{relative}: {exc}")

    sitemap = SITE / "sitemap.xml"
    if sitemap.exists():
        sitemap_text = sitemap.read_text(encoding="utf-8")
        for page in ("smart-reporting.html", "features.html", "control.html", "architecture.html", "roadmap.html"):
            expected = f"{CANONICAL_ROOT}{page}"
            if expected not in sitemap_text:
                errors.append(f"landing/sitemap.xml: missing {expected}")

    robots = SITE / "robots.txt"
    sitemap_declaration = f"Sitemap: {CANONICAL_ROOT}sitemap.xml"
    if not robots.exists() or sitemap_declaration not in robots.read_text(encoding="utf-8"):
        errors.append("landing/robots.txt: missing canonical sitemap declaration")


def validate_unique_metadata(errors: list[str]) -> None:
    titles: dict[str, Path] = {}
    descriptions: dict[str, Path] = {}
    for page in HTML_FILES:
        if not page.exists() or page.name == "404.html":
            continue
        text = page.read_text(encoding="utf-8")
        title_match = re.search(r"<title>(.*?)</title>", text, flags=re.IGNORECASE | re.DOTALL)
        description_match = re.search(
            r'<meta\s+name="description"\s+content="([^"]+)"', text, flags=re.IGNORECASE
        )
        if title_match:
            value = re.sub(r"\s+", " ", title_match.group(1)).strip()
            if value in titles:
                errors.append(f"Duplicate title in {page.relative_to(ROOT)} and {titles[value].relative_to(ROOT)}")
            titles[value] = page
        if description_match:
            value = description_match.group(1).strip()
            if value in descriptions:
                errors.append(
                    f"Duplicate meta description in {page.relative_to(ROOT)} and {descriptions[value].relative_to(ROOT)}"
                )
            descriptions[value] = page


def main() -> int:
    errors: list[str] = []
    for page in HTML_FILES:
        validate_html(page, errors)
    validate_structured_files(errors)
    validate_unique_metadata(errors)

    if errors:
        print("Landing-page validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Landing-page validation passed: HTML, links, metadata, JSON, XML, SVG, branding, and canonical URLs are consistent.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
