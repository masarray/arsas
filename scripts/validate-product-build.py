#!/usr/bin/env python3
"""Validate the rendered ARSAS product website."""

from __future__ import annotations

import json
import re
import struct
import sys
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlparse

EXPECTED_MEDIA = (
    "assets/app-icon.png", "assets/social-card.png",
    "assets/screenshots/arsas-first-launch.webp",
    "assets/screenshots/arsas-multi-ied.webp",
    "assets/screenshots/arsas-live-values.webp",
    "assets/screenshots/arsas-event-log.webp",
    "assets/screenshots/arsas-goose.webp",
    "assets/screenshots/arsas-diagnostics.webp",
    "assets/screenshots/arsas-rcb-scl-export.webp",
)
INSTALLER = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe"
PORTABLE = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip"
CHECKSUMS = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SHA256SUMS.txt"
REPOSITORY = "https://github.com/masarray/arsas"
LINKEDIN = "https://www.linkedin.com/in/ari-sulistiono"
AUTHOR_GITHUB = "https://github.com/masarray"
APP_ICON = "assets/app-icon.png"
EXPECTED_NAV = {"overview", "capabilities", "solutions", "guides", "architecture", "about", "download"}
GUIDE_PAGES = {
    "reporting-silent.html",
    "brcb-vs-urcb.html",
    "rcb-reserved.html",
    "empty-dataset.html",
    "port-102-connection-failed.html",
    "comtrade-download.html",
    "goose-sequence.html",
    "cid-rejected.html",
    "live-model-vs-scl.html",
    "direct-vs-sbo.html",
    "commandtermination-addcause.html",
}
FORBIDDEN_PUBLIC_COPY = (
    "without navigating source code",
    "without navigating the source repository",
    "without requiring repository navigation",
    "the website is the product front door",
)


class Parser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.refs: list[str] = []
        self.icons: list[dict[str, str | None]] = []
        self.h1 = 0
        self.title = ""
        self.in_title = False
        self.description: str | None = None
        self.body_page: str | None = None
        self.nav_pages: set[str] = set()

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "title":
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
        if tag == "link" and "icon" in (values.get("rel") or "").lower():
            self.icons.append(values)

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
    if info.get("schemaVersion") != 2:
        errors.append("build-info.json schemaVersion must be 2")
    version = str(info.get("version", ""))
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        errors.append("build-info.json version is invalid")
    if info.get("repository") != REPOSITORY:
        errors.append("build-info.json repository is invalid")
    author = info.get("author")
    if not isinstance(author, dict) or author.get("name") != "Ari Sulistiono":
        errors.append("build-info.json author is invalid")
    pages = info.get("pages")
    if not isinstance(pages, list) or not pages or not all(isinstance(page, str) for page in pages):
        errors.append("build-info.json pages registry is invalid")
        return version or None, []
    if len(pages) != len(set(pages)):
        errors.append("build-info.json pages registry contains duplicates")
    if len(pages) != 31:
        errors.append(f"build-info.json must contain 31 pages, found {len(pages)}")
    if not GUIDE_PAGES.issubset(set(pages)):
        errors.append("build-info.json is missing troubleshooting guide pages")
    return version or None, list(pages)


def validate_sitemap(site: Path, pages: list[str], errors: list[str]) -> None:
    path = site / "sitemap.xml"
    if not path.exists():
        errors.append("missing sitemap.xml")
        return
    text = path.read_text(encoding="utf-8")
    root = "https://masarray.github.io/arsas/"
    for page in pages:
        if page == "404.html":
            if root + page in text:
                errors.append("sitemap.xml must not index 404.html")
            continue
        url = root if page == "index.html" else root + page
        if url not in text:
            errors.append(f"sitemap.xml missing {page}")


def validate_manifest(site: Path, errors: list[str]) -> None:
    path = site / "site.webmanifest"
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"site.webmanifest: {exc}")
        return
    icons = manifest.get("icons")
    if not isinstance(icons, list) or len(icons) != 1:
        errors.append("site.webmanifest must define one canonical icon")
        return
    icon = icons[0]
    if not isinstance(icon, dict):
        errors.append("site.webmanifest icon is invalid")
        return
    if icon.get("src") != APP_ICON or icon.get("sizes") != "512x512" or icon.get("type") != "image/png":
        errors.append("site.webmanifest must use the 512x512 ARSAS app icon")
    if "maskable" not in str(icon.get("purpose", "")):
        errors.append("site.webmanifest icon must support maskable purpose")


def main() -> int:
    site = Path(sys.argv[1] if len(sys.argv) > 1 else "_site").resolve()
    errors: list[str] = []

    version, pages = validate_build_info(site, errors)
    for relative in tuple(pages) + EXPECTED_MEDIA + ("site.json", "build-info.json", "sitemap.xml", "site.webmanifest"):
        if not (site / relative).exists():
            errors.append(f"missing deployable file: {relative}")

    combined = ""
    for name in pages:
        page = site / name
        if not page.exists():
            continue
        text = page.read_text(encoding="utf-8")
        combined += text
        parser = Parser()
        parser.feed(text)
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

        favicon = [icon for icon in parser.icons if (icon.get("rel") or "").lower() == "icon"]
        if len(favicon) != 1 or favicon[0].get("href") != APP_ICON or favicon[0].get("sizes") != "512x512":
            errors.append(f"{name}: favicon must use the latest 512x512 {APP_ICON}")
        touch = [icon for icon in parser.icons if (icon.get("rel") or "").lower() == "apple-touch-icon"]
        if len(touch) != 1 or touch[0].get("href") != APP_ICON:
            errors.append(f"{name}: apple-touch-icon must use {APP_ICON}")
        if name != "404.html":
            for value in (LINKEDIN, REPOSITORY, 'href="download.html"'):
                if value not in text:
                    errors.append(f"{name}: shared product footer or download route missing {value}")
        if name in GUIDE_PAGES:
            if parser.body_page != "guides":
                errors.append(f"{name}: troubleshooting guide must activate Guides navigation")
            if '"@type":"TechArticle"' not in text.replace(" ", ""):
                errors.append(f"{name}: missing TechArticle structured data")
            for value in ("Engineering boundary", "Written and reviewed by Ari Sulistiono", 'href="guides.html"'):
                if value not in text:
                    errors.append(f"{name}: missing guide trust contract {value}")

    for forbidden in (
        "raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot",
        "https://masarray.github.io/arsas/assets/social-card.svg",
        'href="assets/favicon.svg"',
        "{{", "github.com/masarray/arsas#quick-start", '<meta name="keywords"',
        *FORBIDDEN_PUBLIC_COPY,
    ):
        if forbidden in combined.lower() if forbidden in FORBIDDEN_PUBLIC_COPY else forbidden in combined:
            errors.append(f"deployable HTML contains forbidden value: {forbidden}")

    home = (site / "index.html").read_text(encoding="utf-8") if (site / "index.html").exists() else ""
    download = (site / "download.html").read_text(encoding="utf-8") if (site / "download.html").exists() else ""
    about = (site / "about.html").read_text(encoding="utf-8") if (site / "about.html").exists() else ""
    solutions = (site / "solutions.html").read_text(encoding="utf-8") if (site / "solutions.html").exists() else ""
    guides = (site / "guides.html").read_text(encoding="utf-8") if (site / "guides.html").exists() else ""

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
        if f'href="{guide}"' not in guides:
            errors.append(f"guides hub missing {guide}")
    if version and f'"softwareVersion":"{version}"' not in home.replace(" ", ""):
        errors.append("homepage softwareVersion does not match build-info.json")

    validate_sitemap(site, pages, errors)
    validate_latest(site, errors)
    validate_manifest(site, errors)

    icon = site / APP_ICON
    social = site / "assets/social-card.png"
    for path, expected in ((icon, (512, 512)), (social, (1200, 630))):
        if path.exists():
            try:
                actual = png_size(path)
                if actual != expected:
                    errors.append(f"{path.name}: expected {expected}, found {actual}")
            except ValueError as exc:
                errors.append(f"{path.name}: {exc}")

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS product-build validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print(f"ARSAS product-build validation passed: {len(pages)} pages, 11 troubleshooting guides and latest 512px favicon.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
