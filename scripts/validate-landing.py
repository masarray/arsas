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
PUBLIC_PAGES = [page for page in HTML_FILES if page.name != "404.html"]
LEGACY_TERMS = ("ArIED 61850", "ArIED61850Tester", "ArIED61850")


class PageParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.title_parts: list[str] = []
        self.in_title = False
        self.h1_count = 0
        self.description: str | None = None
        self.canonical: str | None = None
        self.links: list[str] = []
        self.images: list[dict[str, str | None]] = []
        self.scripts: list[str] = []
        self.stylesheets: list[str] = []
        self.meta: dict[str, str] = {}
        self.json_ld: list[str] = []
        self._in_json_ld = False
        self._json_ld_parts: list[str] = []
        self.visible_text: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "title":
            self.in_title = True
        elif tag == "h1":
            self.h1_count += 1
        elif tag == "meta":
            key = values.get("name") or values.get("property")
            content = values.get("content")
            if key and content:
                self.meta[key.lower()] = content
            if values.get("name", "").lower() == "description":
                self.description = content
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
            self.images.append(values)
        elif tag == "script":
            if values.get("src"):
                self.scripts.append(values["src"] or "")
            if values.get("type", "").lower() == "application/ld+json":
                self._in_json_ld = True
                self._json_ld_parts = []

    def handle_endtag(self, tag: str) -> None:
        if tag == "title":
            self.in_title = False
        elif tag == "script" and self._in_json_ld:
            self.json_ld.append("".join(self._json_ld_parts).strip())
            self._in_json_ld = False
            self._json_ld_parts = []

    def handle_data(self, data: str) -> None:
        if self.in_title:
            self.title_parts.append(data)
        if self._in_json_ld:
            self._json_ld_parts.append(data)
        elif data.strip():
            self.visible_text.append(data.strip())


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


def walk_json(value: object):
    if isinstance(value, dict):
        yield value
        for child in value.values():
            yield from walk_json(child)
    elif isinstance(value, list):
        for child in value:
            yield from walk_json(child)


def validate_json_ld(page: Path, parser: PageParser, errors: list[str]) -> None:
    visible = " ".join(parser.visible_text)
    for index, raw in enumerate(parser.json_ld, start=1):
        try:
            payload = json.loads(raw)
        except json.JSONDecodeError as exc:
            errors.append(f"{page.relative_to(ROOT)}: invalid JSON-LD block {index}: {exc}")
            continue
        for node in walk_json(payload):
            if node.get("@type") == "FAQPage":
                for item in node.get("mainEntity", []):
                    question = item.get("name", "") if isinstance(item, dict) else ""
                    if question and question not in visible:
                        errors.append(f"{page.relative_to(ROOT)}: FAQ schema question is not visible: {question}")


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
        errors.append(f"{page.relative_to(ROOT)}: title longer than 75 characters ({len(title)})")

    if page.name != "404.html":
        if parser.h1_count != 1:
            errors.append(f"{page.relative_to(ROOT)}: expected exactly one h1, found {parser.h1_count}")
        if not parser.description:
            errors.append(f"{page.relative_to(ROOT)}: missing meta description")
        elif not 70 <= len(parser.description) <= 220:
            errors.append(f"{page.relative_to(ROOT)}: meta description must be 70-220 characters, found {len(parser.description)}")
        if not parser.canonical or not parser.canonical.startswith(CANONICAL_ROOT):
            errors.append(f"{page.relative_to(ROOT)}: missing or invalid canonical URL")
        if "smart-reporting.html" not in parser.links:
            errors.append(f"{page.relative_to(ROOT)}: Smart Reporting is not linked in static HTML")
        if "audit.css" not in parser.stylesheets:
            errors.append(f"{page.relative_to(ROOT)}: audit.css is not loaded")
        for required in ("og:title", "og:description", "og:url", "og:image", "og:image:width", "og:image:height"):
            if not parser.meta.get(required):
                errors.append(f"{page.relative_to(ROOT)}: missing {required}")

    for reference in parser.links + parser.scripts + parser.stylesheets:
        validate_local_reference(page, reference, errors)

    for image in parser.images:
        src = image.get("src") or ""
        validate_local_reference(page, src, errors)
        if image.get("alt") is None:
            errors.append(f"{page.relative_to(ROOT)}: image missing alt text: {src}")
        if not image.get("width") or not image.get("height"):
            errors.append(f"{page.relative_to(ROOT)}: image missing width/height: {src}")

    if "http://" in text:
        errors.append(f"{page.relative_to(ROOT)}: insecure http:// reference")
    for legacy in LEGACY_TERMS:
        if legacy in text:
            errors.append(f"{page.relative_to(ROOT)}: contains legacy branding: {legacy}")

    validate_json_ld(page, parser, errors)


def read_project_version(errors: list[str]) -> str | None:
    project = ROOT / "ArIED61850Tester.csproj"
    try:
        root = ET.parse(project).getroot()
    except (OSError, ET.ParseError) as exc:
        errors.append(f"ArIED61850Tester.csproj: {exc}")
        return None
    version = root.findtext(".//Version")
    if not version:
        errors.append("ArIED61850Tester.csproj: Version is missing")
    return version


def validate_structured_files(errors: list[str]) -> None:
    manifest_path = SITE / "site.webmanifest"
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        for required in ("name", "short_name", "description", "start_url", "display", "theme_color"):
            if not manifest.get(required):
                errors.append(f"landing/site.webmanifest: missing {required}")
        for icon in manifest.get("icons", []):
            src = icon.get("src", "")
            if is_external(src):
                errors.append(f"landing/site.webmanifest: icon must be local: {src}")
            else:
                validate_local_reference(manifest_path, src, errors)
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"landing/site.webmanifest: {exc}")

    for relative in ("sitemap.xml", "assets/social-card.svg", "assets/favicon.svg"):
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
    declaration = f"Sitemap: {CANONICAL_ROOT}sitemap.xml"
    if not robots.exists() or declaration not in robots.read_text(encoding="utf-8"):
        errors.append("landing/robots.txt: missing canonical sitemap declaration")

    app_js = SITE / "app.js"
    app_text = app_js.read_text(encoding="utf-8") if app_js.exists() else ""
    for forbidden in ("insertAdjacentHTML", "setMeta(", "smartReportingSection", "replaceChildren"):
        if forbidden in app_text:
            errors.append(f"landing/app.js: content or metadata injection is forbidden: {forbidden}")

    version = read_project_version(errors)
    if version:
        homepage = (SITE / "index.html").read_text(encoding="utf-8")
        reporting = (SITE / "smart-reporting.html").read_text(encoding="utf-8")
        marker = f'"softwareVersion": "{version}"'
        compact_marker = f'"softwareVersion":"{version}"'
        if marker not in homepage:
            errors.append(f"landing/index.html: softwareVersion does not match project version {version}")
        if marker not in reporting and compact_marker not in reporting:
            errors.append(f"landing/smart-reporting.html: softwareVersion does not match project version {version}")


def validate_text_assets(errors: list[str]) -> None:
    for path in SITE.rglob("*"):
        if path.is_file() and path.suffix.lower() in {".html", ".css", ".js", ".json", ".xml", ".svg", ".txt"}:
            text = path.read_text(encoding="utf-8")
            for legacy in LEGACY_TERMS:
                if legacy in text:
                    errors.append(f"{path.relative_to(ROOT)}: contains legacy branding: {legacy}")


def validate_unique_metadata(errors: list[str]) -> None:
    titles: dict[str, Path] = {}
    descriptions: dict[str, Path] = {}
    for page in PUBLIC_PAGES:
        text = page.read_text(encoding="utf-8")
        title_match = re.search(r"<title>(.*?)</title>", text, flags=re.IGNORECASE | re.DOTALL)
        description_match = re.search(r'<meta\s+name="description"\s+content="([^"]+)"', text, flags=re.IGNORECASE)
        if title_match:
            value = re.sub(r"\s+", " ", title_match.group(1)).strip()
            if value in titles:
                errors.append(f"Duplicate title in {page.relative_to(ROOT)} and {titles[value].relative_to(ROOT)}")
            titles[value] = page
        if description_match:
            value = description_match.group(1).strip()
            if value in descriptions:
                errors.append(f"Duplicate meta description in {page.relative_to(ROOT)} and {descriptions[value].relative_to(ROOT)}")
            descriptions[value] = page


def main() -> int:
    errors: list[str] = []
    for page in HTML_FILES:
        validate_html(page, errors)
    validate_structured_files(errors)
    validate_text_assets(errors)
    validate_unique_metadata(errors)

    unique_errors = list(dict.fromkeys(errors))
    if unique_errors:
        print("Landing-page validation failed:", file=sys.stderr)
        for error in unique_errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Landing-page validation passed: static content, links, images, metadata, JSON-LD, manifest, branding, version, sitemap, and accessibility guards are consistent.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
