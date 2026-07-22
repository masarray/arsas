#!/usr/bin/env python3
"""Validate the rendered ARSAS product website, localization and release trust."""

from __future__ import annotations

import json
import re
import struct
import sys
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlparse

CANONICAL_ROOT = "https://masarray.github.io/arsas/"
INSTALLER = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe"
PORTABLE = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip"
CHECKSUMS = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SHA256SUMS.txt"
GUIDES = {
    "reporting-silent.html", "brcb-vs-urcb.html", "rcb-reserved.html", "empty-dataset.html",
    "port-102-connection-failed.html", "comtrade-download.html", "goose-sequence.html",
    "cid-rejected.html", "live-model-vs-scl.html", "direct-vs-sbo.html", "commandtermination-addcause.html",
}
PAIRS = {
    "index.html": "id.html", "download.html": "unduh.html", "release-notes.html": "catatan-rilis.html",
    "quick-start.html": "panduan-mulai-arsas.html", "faq.html": "faq-arsas.html",
    "compatibility.html": "bukti-kompatibilitas.html", "demo.html": "demo-arsas.html",
    "guides.html": "panduan.html", "mms-client.html": "mms-client-iec61850.html",
    "smart-reporting.html": "smart-reporting-iec61850.html", "goose-analyzer.html": "analyzer-goose-iec61850.html",
    "file-transfer.html": "transfer-file-comtrade-iec61850.html", "scl-workspace.html": "workspace-scl-iec61850.html",
    "fat-testing.html": "pengujian-fat-iec61850.html", "sat-testing.html": "pengujian-sat-iec61850.html",
    "commissioning.html": "commissioning-iec61850.html", "multi-vendor-integration.html": "integrasi-multi-vendor-iec61850.html",
}
INDEXNOW_FILE = "arsas-iec61850-20260720-6f4a9d2c8b.txt"


class Audit(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.lang = ""
        self.title = ""
        self.in_title = False
        self.h1 = 0
        self.description = ""
        self.body_page = ""
        self.refs: list[str] = []
        self.images: list[dict[str, str | None]] = []
        self.alternates: dict[str, str] = {}
        self.nav: set[str] = set()

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "html": self.lang = values.get("lang") or ""
        if tag == "title": self.in_title = True
        if tag == "h1": self.h1 += 1
        if tag == "body": self.body_page = values.get("data-page") or ""
        if tag == "meta" and (values.get("name") or "").lower() == "description": self.description = values.get("content") or ""
        if tag == "a" and values.get("data-nav-page"): self.nav.add(values.get("data-nav-page") or "")
        if tag == "link" and values.get("rel") == "alternate" and values.get("hreflang"): self.alternates[values.get("hreflang") or ""] = values.get("href") or ""
        for key in ("href", "src"):
            if values.get(key): self.refs.append(values[key] or "")
        if tag == "img": self.images.append(values)

    def handle_endtag(self, tag: str) -> None:
        if tag == "title": self.in_title = False

    def handle_data(self, data: str) -> None:
        if self.in_title: self.title += data


def png_size(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n": raise ValueError("not a PNG")
    return struct.unpack(">II", data[16:24])


def page_url(path: str) -> str:
    return CANONICAL_ROOT if path == "index.html" else CANONICAL_ROOT + path


def local_target(site: Path, page: Path, reference: str) -> Path | None:
    clean = reference.split("#", 1)[0].split("?", 1)[0]
    parsed = urlparse(clean)
    if not clean or parsed.scheme or parsed.netloc or clean.startswith("#"): return None
    return (page.parent / clean).resolve()


def main() -> int:
    site = Path(sys.argv[1] if len(sys.argv) > 1 else "_site").resolve()
    errors: list[str] = []
    try:
        registry = json.loads((site / "site.json").read_text(encoding="utf-8"))
        info = json.loads((site / "build-info.json").read_text(encoding="utf-8"))
        latest = json.loads((site / "latest.json").read_text(encoding="utf-8"))
        notes = json.loads((site / "release-notes.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"ARSAS rendered validation failed:\n- core JSON: {exc}", file=sys.stderr)
        return 1

    entries = registry.get("pages", []) if isinstance(registry, dict) else []
    expected_pages = [str(item.get("path") or "index.html") for item in entries if isinstance(item, dict)]
    if len(expected_pages) != 54 or len(expected_pages) != len(set(expected_pages)): errors.append("site registry must contain 54 unique pages")
    if info.get("schemaVersion") != 3 or info.get("pages") != expected_pages: errors.append("build-info page registry does not match site.json")
    if info.get("languages") != ["en", "id"]: errors.append("build-info languages must be en and id")
    if info.get("repository") != "https://github.com/masarray/arsas": errors.append("build-info repository is invalid")
    if info.get("indexNowKeyLocation") != CANONICAL_ROOT + INDEXNOW_FILE: errors.append("build-info IndexNow location is invalid")
    if latest.get("version") != notes.get("version") or latest.get("channel") != "stable": errors.append("stable release JSON is inconsistent")
    for key, url in (("installer", INSTALLER), ("portable", PORTABLE)):
        item = latest.get(key)
        if not isinstance(item, dict) or item.get("url") != url or not re.fullmatch(r"[0-9a-fA-F]{64}", str(item.get("sha256", ""))): errors.append(f"latest.json {key} evidence is invalid")
    if not isinstance(latest.get("checksums"), dict) or latest["checksums"].get("url") != CHECKSUMS: errors.append("latest.json checksum URL is invalid")

    entry_map = {str(item.get("path") or "index.html"): item for item in entries if isinstance(item, dict)}
    rendered: dict[str, Audit] = {}
    for name in expected_pages:
        page = site / name
        if not page.is_file():
            errors.append(f"missing rendered page {name}")
            continue
        text = page.read_text(encoding="utf-8")
        if "{{" in text: errors.append(f"{name}: unresolved template token")
        audit = Audit(); audit.feed(text); rendered[name] = audit
        if not audit.title.strip() or audit.h1 != 1 or not 60 <= len(audit.description) <= 260: errors.append(f"{name}: metadata or h1 contract failed")
        language = str(entry_map[name].get("language", "en"))
        if audit.lang != language: errors.append(f"{name}: html language must be {language}")
        if not audit.body_page: errors.append(f"{name}: body data-page is missing")
        for image in audit.images:
            src = image.get("src") or ""
            if image.get("alt") is None or not image.get("width") or not image.get("height"): errors.append(f"{name}: incomplete image metadata {src}")
            target = local_target(site, page, src)
            if target is not None and not target.is_file(): errors.append(f"{name}: missing image {src}")
        for reference in audit.refs:
            target = local_target(site, page, reference)
            if target is not None and not target.exists(): errors.append(f"{name}: broken local reference {reference}")
        if name != "404.html" and {"overview", "capabilities", "solutions", "guides", "architecture", "about", "download"} - audit.nav: errors.append(f"{name}: shared navigation is incomplete")

    for english, indonesian in PAIRS.items():
        expected = {"en": page_url(english), "id": page_url(indonesian), "x-default": page_url(english)}
        for page in (english, indonesian):
            audit = rendered.get(page)
            if audit and audit.alternates != expected: errors.append(f"{page}: reciprocal hreflang set is invalid")
    if not GUIDES.issubset(set(expected_pages)): errors.append("troubleshooting guides are missing from the build")

    sitemap = site / "sitemap.xml"
    try:
        tree = ET.parse(sitemap)
        ns = {"sm": "http://www.sitemaps.org/schemas/sitemap/0.9", "xhtml": "http://www.w3.org/1999/xhtml"}
        urls = tree.getroot().findall("sm:url", ns)
        locations = [(node.findtext("sm:loc", default="", namespaces=ns) or "").strip() for node in urls]
        expected_indexable = [name for name in expected_pages if entry_map[name].get("index", True) is not False]
        expected_locations = [page_url(name) for name in expected_indexable]
        if locations != expected_locations: errors.append("sitemap URLs do not match indexable registry order")
        if len(locations) != 53: errors.append(f"sitemap must contain 53 URLs, found {len(locations)}")
        if page_url("404.html") in locations: errors.append("404 page must not be in sitemap")
    except (OSError, ET.ParseError) as exc:
        errors.append(f"sitemap.xml: {exc}")

    for required in (
        "assets/app-icon.png", "assets/social-card.png", "assets/screenshots/arsas-first-launch.webp",
        "assets/screenshots/arsas-multi-ied.webp", "assets/screenshots/arsas-live-values.webp",
        "assets/screenshots/arsas-event-log.webp", "assets/screenshots/arsas-goose.webp",
        "assets/screenshots/arsas-diagnostics.webp", "assets/screenshots/arsas-rcb-scl-export.webp",
        "device-evidence.json", "adoption.css", "guide-filter.js", "demo.js", INDEXNOW_FILE,
    ):
        if not (site / required).is_file(): errors.append(f"missing output {required}")
    try:
        width, height = png_size(site / "assets/app-icon.png")
        if width != height or width < 256: errors.append("rendered app icon is invalid")
    except (OSError, ValueError) as exc: errors.append(f"app icon: {exc}")

    combined = "\n".join((site / name).read_text(encoding="utf-8") for name in expected_pages if (site / name).is_file())
    for value in ('href="http://', 'src="http://', "raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot", '<meta name="keywords"'):
        if value in combined: errors.append(f"forbidden public value remains: {value}")
    for value in (INSTALLER, PORTABLE, CHECKSUMS, "Ari Sulistiono", "GPL-3.0-or-later"):
        if value not in combined: errors.append(f"public site missing trust value {value}")

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS rendered validation failed:", file=sys.stderr)
        for error in errors: print(f"- {error}", file=sys.stderr)
        return 1
    print("ARSAS rendered validation passed: 54 pages, 53 sitemap URLs, 17 localized pairs, release trust, links and media.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
