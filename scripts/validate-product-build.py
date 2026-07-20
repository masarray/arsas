#!/usr/bin/env python3
"""Validate the rendered ARSAS product website."""

from __future__ import annotations

import json
import re
import struct
import sys
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlparse

EXPECTED_MEDIA = (
    "assets/app-icon.png", "assets/social-card.png",
    "assets/screenshots/arsas-first-launch.webp", "assets/screenshots/arsas-multi-ied.webp",
    "assets/screenshots/arsas-live-values.webp", "assets/screenshots/arsas-event-log.webp",
    "assets/screenshots/arsas-goose.webp", "assets/screenshots/arsas-diagnostics.webp",
    "assets/screenshots/arsas-rcb-scl-export.webp",
)
INSTALLER = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe"
PORTABLE = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip"
CHECKSUMS = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SHA256SUMS.txt"
REPOSITORY = "https://github.com/masarray/arsas"
LINKEDIN = "https://www.linkedin.com/in/ari-sulistiono"
AUTHOR_GITHUB = "https://github.com/masarray"
CANONICAL_ROOT = "https://masarray.github.io/arsas/"
APP_ICON = "assets/app-icon.png"
INDEXNOW_KEY = "arsas-iec61850-20260720-6f4a9d2c8b"
INDEXNOW_FILE = INDEXNOW_KEY + ".txt"
EXPECTED_NAV = {"overview", "capabilities", "solutions", "guides", "architecture", "about", "download"}
GUIDE_PAGES = {
    "reporting-silent.html", "brcb-vs-urcb.html", "rcb-reserved.html",
    "empty-dataset.html", "port-102-connection-failed.html", "comtrade-download.html",
    "goose-sequence.html", "cid-rejected.html", "live-model-vs-scl.html",
    "direct-vs-sbo.html", "commandtermination-addcause.html",
}
LOCALIZED_PAIRS = {
    "index.html": "id.html",
    "download.html": "unduh.html",
    "guides.html": "panduan.html",
    "mms-client.html": "mms-client-iec61850.html",
    "smart-reporting.html": "smart-reporting-iec61850.html",
    "goose-analyzer.html": "analyzer-goose-iec61850.html",
    "file-transfer.html": "transfer-file-comtrade-iec61850.html",
    "scl-workspace.html": "workspace-scl-iec61850.html",
    "fat-testing.html": "pengujian-fat-iec61850.html",
    "sat-testing.html": "pengujian-sat-iec61850.html",
    "commissioning.html": "commissioning-iec61850.html",
    "multi-vendor-integration.html": "integrasi-multi-vendor-iec61850.html",
}
LOCALIZED_PAGES = set(LOCALIZED_PAIRS.values())
FORBIDDEN_COPY = (
    "without navigating source code", "without navigating the source repository",
    "without requiring repository navigation", "the website is the product front door",
)


class Parser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.refs: list[str] = []
        self.icons: list[dict[str, str | None]] = []
        self.images: list[dict[str, str | None]] = []
        self.alternates: dict[str, str] = {}
        self.h1 = 0
        self.title = ""
        self.in_title = False
        self.description: str | None = None
        self.body_page: str | None = None
        self.nav_pages: set[str] = set()
        self.html_lang: str | None = None

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "html":
            self.html_lang = values.get("lang")
        elif tag == "title":
            self.in_title = True
        elif tag == "h1":
            self.h1 += 1
        elif tag == "body":
            self.body_page = values.get("data-page")
        elif tag == "meta" and values.get("name", "").lower() == "description":
            self.description = values.get("content")
        if tag == "a" and values.get("data-nav-page"):
            self.nav_pages.add(values["data-nav-page"] or "")
        for key in ("href", "src"):
            value = values.get(key)
            if value:
                self.refs.append(value)
        if tag == "link":
            rel = (values.get("rel") or "").lower()
            if "icon" in rel:
                self.icons.append(values)
            if rel == "alternate" and values.get("hreflang") and values.get("href"):
                self.alternates[values["hreflang"] or ""] = values["href"] or ""
        if tag == "img":
            self.images.append(values)

    def handle_endtag(self, tag: str) -> None:
        if tag == "title":
            self.in_title = False

    def handle_data(self, data: str) -> None:
        if self.in_title:
            self.title += data


def png_size(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG")
    return struct.unpack(">II", data[16:24])


def local_ref(site: Path, page: Path, reference: str) -> Path | None:
    clean = reference.split("#", 1)[0].split("?", 1)[0]
    parsed = urlparse(clean)
    if not clean or parsed.scheme or parsed.netloc or clean.startswith("#"):
        return None
    return (page.parent / clean).resolve()


def page_url(name: str) -> str:
    return CANONICAL_ROOT if name == "index.html" else CANONICAL_ROOT + name


def validate_latest(site: Path, errors: list[str]) -> None:
    path = site / "latest.json"
    if not path.exists():
        errors.append("missing latest.json")
        return
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"latest.json: {exc}")
        return
    if manifest.get("schemaVersion") != 1 or manifest.get("product") != "ARSAS":
        errors.append("latest.json has invalid identity")
    if manifest.get("channel") != "stable":
        errors.append("latest.json channel must be stable")
    if not re.fullmatch(r"\d+\.\d+\.\d+", str(manifest.get("version", ""))):
        errors.append("latest.json version is invalid")
    installer = manifest.get("installer")
    if not isinstance(installer, dict) or installer.get("url") != INSTALLER:
        errors.append("latest.json installer URL is invalid")
    elif not re.fullmatch(r"[0-9a-fA-F]{64}", str(installer.get("sha256", ""))):
        errors.append("latest.json installer SHA-256 is invalid")


def validate_build_info(site: Path, errors: list[str]) -> tuple[str | None, list[str]]:
    path = site / "build-info.json"
    try:
        info = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"build-info.json: {exc}")
        return None, []
    if info.get("schemaVersion") != 3:
        errors.append("build-info.json schemaVersion must be 3")
    version = str(info.get("version", ""))
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        errors.append("build-info.json version is invalid")
    if info.get("repository") != REPOSITORY:
        errors.append("build-info.json repository is invalid")
    author = info.get("author")
    if not isinstance(author, dict) or author.get("name") != "Ari Sulistiono":
        errors.append("build-info.json author is invalid")
    if info.get("languages") != ["en", "id"]:
        errors.append("build-info.json languages must be en and id")
    if info.get("indexNowKeyLocation") != CANONICAL_ROOT + INDEXNOW_FILE:
        errors.append("build-info.json IndexNow key location is invalid")
    pages = info.get("pages")
    if not isinstance(pages, list) or not pages or not all(isinstance(page, str) for page in pages):
        errors.append("build-info.json pages registry is invalid")
        return version or None, []
    if len(pages) != len(set(pages)):
        errors.append("build-info.json pages registry contains duplicates")
    if len(pages) != 44:
        errors.append(f"build-info.json must contain 44 pages, found {len(pages)}")
    if not GUIDE_PAGES.issubset(set(pages)):
        errors.append("build-info.json is missing troubleshooting guide pages")
    if not LOCALIZED_PAGES.issubset(set(pages)) or "technical-review.html" not in pages:
        errors.append("build-info.json is missing authority or Indonesian pages")
    return version or None, list(pages)


def validate_sitemap(site: Path, pages: list[str], errors: list[str]) -> None:
    path = site / "sitemap.xml"
    if not path.exists():
        errors.append("missing sitemap.xml")
        return
    try:
        tree = ET.parse(path)
    except ET.ParseError as exc:
        errors.append(f"sitemap.xml parsing failed: {exc}")
        return
    ns = {"sm": "http://www.sitemaps.org/schemas/sitemap/0.9", "xhtml": "http://www.w3.org/1999/xhtml"}
    root = tree.getroot()
    if root.tag != "{http://www.sitemaps.org/schemas/sitemap/0.9}urlset":
        errors.append("sitemap.xml has an invalid root namespace")
        return
    records: dict[str, dict[str, str]] = {}
    for node in root.findall("sm:url", ns):
        loc_node = node.find("sm:loc", ns)
        if loc_node is None or not (loc_node.text or "").strip():
            errors.append("sitemap.xml contains a URL without loc")
            continue
        loc = (loc_node.text or "").strip()
        if loc in records:
            errors.append(f"sitemap.xml contains duplicate loc {loc}")
        alternates: dict[str, str] = {}
        for link in node.findall("xhtml:link", ns):
            language, href = link.get("hreflang"), link.get("href")
            if language and href:
                alternates[language] = href
        records[loc] = alternates

    expected_urls = {page_url(page) for page in pages if page != "404.html"}
    if set(records) != expected_urls:
        for missing in sorted(expected_urls - set(records)):
            errors.append(f"sitemap.xml missing {missing}")
        for extra in sorted(set(records) - expected_urls):
            errors.append(f"sitemap.xml contains unexpected URL {extra}")
    if len(records) != 43:
        errors.append(f"sitemap.xml must contain 43 indexable URLs, found {len(records)}")

    for english, indonesian in LOCALIZED_PAIRS.items():
        expected = {
            "en": page_url(english),
            "id": page_url(indonesian),
            "x-default": page_url(english),
        }
        for page in (english, indonesian):
            if records.get(page_url(page)) != expected:
                errors.append(f"sitemap.xml localized alternate set is incomplete for {page}")


def validate_manifest(site: Path, icon_size: str, errors: list[str]) -> None:
    path = site / "site.webmanifest"
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"site.webmanifest: {exc}")
        return
    icons = manifest.get("icons")
    if not isinstance(icons, list) or len(icons) != 1 or not isinstance(icons[0], dict):
        errors.append("site.webmanifest must define one canonical icon")
        return
    icon = icons[0]
    if icon.get("src") != APP_ICON or icon.get("sizes") != icon_size or icon.get("type") != "image/png":
        errors.append(f"site.webmanifest must use the actual {icon_size} ARSAS app icon")
    if "maskable" not in str(icon.get("purpose", "")):
        errors.append("site.webmanifest icon must support maskable purpose")


def expected_alternates_for(name: str) -> dict[str, str] | None:
    for english, indonesian in LOCALIZED_PAIRS.items():
        if name in (english, indonesian):
            return {
                "en": page_url(english),
                "id": page_url(indonesian),
                "x-default": page_url(english),
            }
    return None


def main() -> int:
    site = Path(sys.argv[1] if len(sys.argv) > 1 else "_site").resolve()
    errors: list[str] = []
    version, pages = validate_build_info(site, errors)
    for relative in tuple(pages) + EXPECTED_MEDIA + ("site.json", "build-info.json", "sitemap.xml", "site.webmanifest", INDEXNOW_FILE):
        if not (site / relative).exists():
            errors.append(f"missing deployable file: {relative}")
    key_path = site / INDEXNOW_FILE
    if key_path.exists() and key_path.read_text(encoding="utf-8").strip() != INDEXNOW_KEY:
        errors.append("deployed IndexNow key file does not match the configured key")

    icon_path = site / APP_ICON
    icon_size = ""
    icon_width = icon_height = 0
    if icon_path.exists():
        try:
            icon_width, icon_height = png_size(icon_path)
            icon_size = f"{icon_width}x{icon_height}"
            if icon_width != icon_height or icon_width < 256:
                errors.append(f"app-icon.png must be square and at least 256px, found {icon_size}")
        except ValueError as exc:
            errors.append(f"app-icon.png: {exc}")

    combined = ""
    parsers: dict[str, Parser] = {}
    for name in pages:
        page = site / name
        if not page.exists():
            continue
        text = page.read_text(encoding="utf-8")
        combined += text
        parser = Parser()
        parser.feed(text)
        parsers[name] = parser
        if parser.h1 != 1:
            errors.append(f"{name}: expected one h1")
        if not parser.title.strip() or not parser.description:
            errors.append(f"{name}: title or description is missing")
        if not parser.body_page:
            errors.append(f"{name}: body data-page is missing")
        if parser.nav_pages != EXPECTED_NAV:
            errors.append(f"{name}: shared navigation is incomplete")
        for reference in parser.refs:
            target = local_ref(site, page, reference)
            if target is not None:
                try:
                    target.relative_to(site)
                except ValueError:
                    errors.append(f"{name}: local reference escapes site {reference}")
                    continue
                if not target.exists():
                    errors.append(f"{name}: missing local asset {reference}")
        favicon = [item for item in parser.icons if (item.get("rel") or "").lower() == "icon"]
        if len(favicon) != 1 or favicon[0].get("href") != APP_ICON or favicon[0].get("sizes") != icon_size:
            errors.append(f"{name}: favicon must use the actual {icon_size} {APP_ICON}")
        touch = [item for item in parser.icons if (item.get("rel") or "").lower() == "apple-touch-icon"]
        if len(touch) != 1 or touch[0].get("href") != APP_ICON:
            errors.append(f"{name}: apple-touch-icon must use {APP_ICON}")
        brand_images = [item for item in parser.images if item.get("src") == APP_ICON]
        if name != "404.html":
            if len(brand_images) < 2:
                errors.append(f"{name}: header and footer must use app-icon.png brand marks")
            for image in brand_images:
                if image.get("width") != str(icon_width) or image.get("height") != str(icon_height):
                    errors.append(f"{name}: brand mark metadata must match {icon_size}")
            for value in (LINKEDIN, REPOSITORY, 'href="download.html"', 'href="technical-review.html"'):
                if value not in text:
                    errors.append(f"{name}: shared authority or download route missing {value}")
        expected_alternates = expected_alternates_for(name)
        if expected_alternates is not None and parser.alternates != expected_alternates:
            errors.append(f"{name}: page-level hreflang alternates are incomplete")
        elif expected_alternates is None and parser.alternates:
            errors.append(f"{name}: unexpected page-level language alternates")
        if name in GUIDE_PAGES:
            if parser.body_page != "guides":
                errors.append(f"{name}: troubleshooting guide must activate Guides navigation")
            if '"@type":"TechArticle"' not in text.replace(" ", ""):
                errors.append(f"{name}: missing TechArticle structured data")
            for value in ("Engineering boundary", "Written and reviewed by Ari Sulistiono", 'href="guides.html"', 'href="technical-review.html"'):
                if value not in text:
                    errors.append(f"{name}: missing guide trust contract {value}")
        if name in LOCALIZED_PAGES:
            if parser.html_lang != "id" or '"inLanguage":"id"' not in text.replace(" ", ""):
                errors.append(f"{name}: Indonesian language metadata is incomplete")
            if 'hreflang="en"' not in text:
                errors.append(f"{name}: missing English counterpart link")
            for value in ('href="unduh.html"', "Semua opsi unduhan"):
                if value not in text:
                    errors.append(f"{name}: Indonesian download CTA is incomplete: {value}")

    for forbidden in (
        "raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot",
        "https://masarray.github.io/arsas/assets/social-card.svg", 'href="assets/favicon.svg"',
        "{{", "github.com/masarray/arsas#quick-start", '<meta name="keywords"', *FORBIDDEN_COPY,
    ):
        haystack = combined.lower() if forbidden in FORBIDDEN_COPY else combined
        if forbidden in haystack:
            errors.append(f"deployable HTML contains forbidden value: {forbidden}")

    def page_text(name: str) -> str:
        path = site / name
        return path.read_text(encoding="utf-8") if path.exists() else ""

    home, download, about = page_text("index.html"), page_text("download.html"), page_text("about.html")
    solutions, guides = page_text("solutions.html"), page_text("guides.html")
    review, id_home = page_text("technical-review.html"), page_text("id.html")
    id_guides, id_download = page_text("panduan.html"), page_text("unduh.html")
    for value in (INSTALLER, 'href="download.html"', 'href="solutions.html"', "arsas-rcb-scl-export.webp", '"codeRepository"'):
        if value not in home:
            errors.append(f"homepage missing product contract: {value}")
    for value in (INSTALLER, PORTABLE, CHECKSUMS, "Latest stable channel", "Download Center"):
        if value not in download:
            errors.append(f"download page missing {value}")
    for value in (LINKEDIN, AUTHOR_GITHUB, REPOSITORY, "Download Center"):
        if value not in about + home:
            errors.append(f"author or open-source identity missing {value}")
    for value in ('href="fat-testing.html"', 'href="sat-testing.html"', 'href="commissioning.html"', 'href="multi-vendor-integration.html"'):
        if value not in solutions:
            errors.append(f"solutions page missing {value}")
    for guide in GUIDE_PAGES:
        if f'href="{guide}"' not in guides or f'href="{guide}"' not in id_guides:
            errors.append(f"guide hubs missing {guide}")
    for value in ("Claim governance", "Not a conformance certificate", LINKEDIN, REPOSITORY):
        if value not in review:
            errors.append(f"technical review page missing {value}")
    for localized in LOCALIZED_PAGES - {"id.html", "panduan.html", "unduh.html"}:
        if f'href="{localized}"' not in id_home and f'href="{localized}"' not in id_guides:
            errors.append(f"Indonesian hubs do not link to {localized}")
    for value in (INSTALLER, PORTABLE, CHECKSUMS, "Unduh ARSAS", "Semua opsi unduhan"):
        if value not in id_download:
            errors.append(f"Indonesian download page missing {value}")

    localized_contracts = {
        "mms-client-iec61850.html": ("MMS Client", "DataSet"),
        "smart-reporting-iec61850.html": ("BRCB", "URCB"),
        "analyzer-goose-iec61850.html": ("stNum", "sqNum"),
        "transfer-file-comtrade-iec61850.html": ("COMTRADE", "CFG"),
        "workspace-scl-iec61850.html": ("Edition 1", "selected-RCB"),
        "pengujian-fat-iec61850.html": ("Pengujian FAT", "GOOSE"),
        "pengujian-sat-iec61850.html": ("Pengujian SAT", "station"),
        "commissioning-iec61850.html": ("Commissioning", "SOE"),
        "integrasi-multi-vendor-iec61850.html": ("multi-vendor", "selected-RCB"),
    }
    for name, values in localized_contracts.items():
        text = page_text(name)
        for value in values:
            if value not in text:
                errors.append(f"{name}: missing translated technical content {value}")

    if version and f'"softwareVersion":"{version}"' not in home.replace(" ", ""):
        errors.append("homepage softwareVersion does not match build-info.json")

    validate_sitemap(site, pages, errors)
    validate_latest(site, errors)
    if icon_size:
        validate_manifest(site, icon_size, errors)
    social = site / "assets/social-card.png"
    if social.exists():
        try:
            if png_size(social) != (1200, 630):
                errors.append(f"social-card.png: expected 1200x630, found {png_size(social)}")
        except ValueError as exc:
            errors.append(f"social-card.png: {exc}")

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS product-build validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print(f"ARSAS product-build validation passed: {len(pages)} pages, 11 guides, 12 Indonesian pages, 12 hreflang pairs, authority policy, IndexNow and {icon_size} brand artwork.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
