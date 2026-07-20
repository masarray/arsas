#!/usr/bin/env python3
"""Validate ARSAS templates, guide contracts, identity and page registry before build."""

from __future__ import annotations

import json
import re
import struct
import sys
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LANDING = ROOT / "landing"
TEMPLATES = LANDING / "templates"
PARTIALS = LANDING / "partials"
APP_ICON_SOURCE = ROOT / "Assets" / "app-icon.png"
INCLUDE_PATTERN = re.compile(r"\{\{>\s*([a-z0-9-]+)\s*\}\}", re.IGNORECASE)
VERIFICATION_FILE_PATTERN = re.compile(r"google[a-z0-9]+\.html", re.IGNORECASE)
KNOWN_TOKENS = {
    "ARSAS_VERSION", "PRODUCT_NAME", "CANONICAL_ROOT", "REPOSITORY_URL",
    "ENGINE_REPOSITORY_URL", "AUTHOR_NAME", "AUTHOR_LINKEDIN", "AUTHOR_GITHUB",
    "INSTALLER_URL", "PORTABLE_URL", "CHECKSUMS_URL",
}
EXPECTED_NAV = ("overview", "capabilities", "solutions", "guides", "architecture", "about", "download")
FORBIDDEN_PUBLIC_COPY = (
    "without navigating source code",
    "without navigating the source repository",
    "without requiring repository navigation",
    "the website is the product front door",
)


class Parser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.in_title = False
        self.title = ""
        self.h1 = 0
        self.description: str | None = None
        self.meta: dict[str, str] = {}
        self.images: list[dict[str, str | None]] = []
        self.body_page: str | None = None

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "title":
            self.in_title = True
        elif tag == "h1":
            self.h1 += 1
        elif tag == "body":
            self.body_page = values.get("data-page")
        elif tag == "meta":
            key = values.get("name") or values.get("property")
            value = values.get("content")
            if key and value:
                self.meta[key.lower()] = value
            if values.get("name", "").lower() == "description":
                self.description = value
        elif tag == "img":
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


def expand_partials(text: str, errors: list[str], stack: tuple[str, ...] = ()) -> str:
    def replace(match: re.Match[str]) -> str:
        name = match.group(1).lower()
        if name in stack:
            errors.append("circular partial include: " + " -> ".join((*stack, name)))
            return ""
        path = PARTIALS / f"{name}.html"
        if not path.exists():
            errors.append(f"missing partial: {path.relative_to(ROOT)}")
            return ""
        return expand_partials(path.read_text(encoding="utf-8"), errors, (*stack, name))

    previous = None
    while previous != text:
        previous = text
        text = INCLUDE_PATTERN.sub(replace, text)
    return text


def validate_template(path: Path, content_type: str | None, errors: list[str]) -> None:
    if not path.exists():
        errors.append(f"missing template: {path.relative_to(ROOT)}")
        return

    raw = path.read_text(encoding="utf-8")
    text = expand_partials(raw, errors)
    parser = Parser()
    parser.feed(text)
    label = path.relative_to(ROOT)

    if not parser.title.strip() or len(parser.title.strip()) > 75:
        errors.append(f"{label}: invalid title")
    if parser.h1 != 1:
        errors.append(f"{label}: expected one h1, found {parser.h1}")
    if not parser.description or not 70 <= len(parser.description) <= 220:
        errors.append(f"{label}: invalid meta description")
    if not parser.body_page:
        errors.append(f"{label}: missing body data-page")
    for key in ("og:title", "og:description", "og:url", "og:image", "og:image:width", "og:image:height"):
        if not parser.meta.get(key):
            errors.append(f"{label}: missing {key}")
    for image in parser.images:
        src = image.get("src") or ""
        if image.get("alt") is None or not image.get("width") or not image.get("height"):
            errors.append(f"{label}: incomplete image metadata {src}")
        if "{{" not in src and not (LANDING / src).exists():
            errors.append(f"{label}: missing image {src}")

    unknown = set(re.findall(r"\{\{([A-Z0-9_]+)\}\}", text)) - KNOWN_TOKENS
    if unknown:
        errors.append(f"{label}: unknown tokens {sorted(unknown)}")
    if INCLUDE_PATTERN.search(text):
        errors.append(f"{label}: unresolved partial include")
    lowered = text.lower()
    if '<meta name="keywords"' in lowered:
        errors.append(f"{label}: obsolete meta keywords must not be used")
    if "github.com/masarray/arsas#quick-start" in text:
        errors.append(f"{label}: product page routes users to README quick-start")
    for phrase in FORBIDDEN_PUBLIC_COPY:
        if phrase in lowered:
            errors.append(f"{label}: contains internal-strategy copy: {phrase}")

    if path.name != "404.html":
        for include in ("{{> header}}", "{{> footer}}"):
            if include not in raw:
                errors.append(f"{label}: missing shared include {include}")
    if path.name not in ("index.html", "download.html", "404.html") and "{{> download-cta}}" not in raw:
        errors.append(f"{label}: missing shared download CTA")
    if content_type == "guide":
        if '"@type":"TechArticle"' not in raw and '"@type": "TechArticle"' not in raw:
            errors.append(f"{label}: guide must use TechArticle structured data")
        if "{{> guide-boundary}}" not in raw:
            errors.append(f"{label}: guide must include shared engineering boundary")
        if parser.body_page != "guides":
            errors.append(f"{label}: guide must activate Guides navigation")


def read_config(errors: list[str]) -> dict[str, object] | None:
    try:
        config = json.loads((LANDING / "site.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"landing/site.json: {exc}")
        return None

    checks = {
        ("product", "name"): "ARSAS",
        ("product", "repository"): "https://github.com/masarray/arsas",
        ("author", "name"): "Ari Sulistiono",
        ("author", "linkedin"): "https://www.linkedin.com/in/ari-sulistiono",
        ("author", "github"): "https://github.com/masarray",
    }
    for keys, expected in checks.items():
        value: object = config
        for key in keys:
            value = value.get(key) if isinstance(value, dict) else None
        if value != expected:
            errors.append(f"landing/site.json: {'.'.join(keys)} must be {expected}")

    downloads = config.get("downloads")
    for key in ("installer", "portable", "checksums"):
        value = downloads.get(key) if isinstance(downloads, dict) else None
        if not isinstance(value, str) or not value.startswith("https://github.com/masarray/arsas/releases/latest/download/"):
            errors.append(f"landing/site.json: invalid downloads.{key}")
    return config


def validate_registry(config: dict[str, object], errors: list[str]) -> list[tuple[Path, str | None]]:
    pages = config.get("pages")
    if not isinstance(pages, list) or not pages:
        errors.append("landing/site.json: pages registry must be a non-empty list")
        return []

    paths: set[str] = set()
    names: set[str] = set()
    templates: list[tuple[Path, str | None]] = []
    guide_count = 0
    for entry in pages:
        if not isinstance(entry, dict):
            errors.append("landing/site.json: every page entry must be an object")
            continue
        page_path = entry.get("path")
        template = entry.get("template")
        content_type = entry.get("contentType")
        if not isinstance(page_path, str) or not isinstance(template, str):
            errors.append("landing/site.json: every page entry needs string path and template")
            continue
        if page_path in paths:
            errors.append(f"landing/site.json: duplicate page path {page_path}")
        if template in names:
            errors.append(f"landing/site.json: duplicate template {template}")
        paths.add(page_path)
        names.add(template)
        template_path = TEMPLATES / template
        templates.append((template_path, content_type if isinstance(content_type, str) else None))
        if not template_path.exists():
            errors.append(f"landing/site.json: missing registered template {template}")
        if page_path not in ("", "404.html") and not page_path.endswith(".html"):
            errors.append(f"landing/site.json: invalid page path {page_path}")
        if page_path == "404.html" and entry.get("index", True) is not False:
            errors.append("landing/site.json: 404.html must be excluded from sitemap")
        if content_type == "guide":
            guide_count += 1
    if guide_count != 11:
        errors.append(f"landing/site.json: expected 11 troubleshooting guides, found {guide_count}")

    actual = {path.name for path in TEMPLATES.glob("*.html")}
    if actual - names:
        errors.append("templates missing from registry: " + ", ".join(sorted(actual - names)))
    if names - actual:
        errors.append("registry templates missing from disk: " + ", ".join(sorted(names - actual)))
    return templates


def validate_partials(errors: list[str]) -> None:
    header = (PARTIALS / "header.html").read_text(encoding="utf-8") if (PARTIALS / "header.html").exists() else ""
    footer = (PARTIALS / "footer.html").read_text(encoding="utf-8") if (PARTIALS / "footer.html").exists() else ""
    cta = (PARTIALS / "download-cta.html").read_text(encoding="utf-8") if (PARTIALS / "download-cta.html").exists() else ""
    guide_boundary = (PARTIALS / "guide-boundary.html").read_text(encoding="utf-8") if (PARTIALS / "guide-boundary.html").exists() else ""
    for page in EXPECTED_NAV:
        if f'data-nav-page="{page}"' not in header:
            errors.append(f"shared header missing navigation key {page}")
    for value in ("{{AUTHOR_NAME}}", "{{AUTHOR_LINKEDIN}}", "{{REPOSITORY_URL}}", 'href="solutions.html"', 'href="guides.html"'):
        if value not in footer:
            errors.append(f"shared footer missing {value}")
    for value in ("{{ARSAS_VERSION}}", "{{INSTALLER_URL}}", 'href="download.html"'):
        if value not in cta:
            errors.append(f"shared download CTA missing {value}")
    for value in ("Ari Sulistiono", 'href="guides.html"', "Engineering boundary"):
        if value not in guide_boundary:
            errors.append(f"shared guide boundary missing {value}")


def validate_contract(errors: list[str]) -> None:
    home = (TEMPLATES / "index.html").read_text(encoding="utf-8") if (TEMPLATES / "index.html").exists() else ""
    download = (TEMPLATES / "download.html").read_text(encoding="utf-8") if (TEMPLATES / "download.html").exists() else ""
    about = (TEMPLATES / "about.html").read_text(encoding="utf-8") if (TEMPLATES / "about.html").exists() else ""
    solutions = (TEMPLATES / "solutions.html").read_text(encoding="utf-8") if (TEMPLATES / "solutions.html").exists() else ""
    guides = (TEMPLATES / "guides.html").read_text(encoding="utf-8") if (TEMPLATES / "guides.html").exists() else ""

    for value in ("{{INSTALLER_URL}}", 'href="download.html"', 'href="solutions.html"', "arsas-rcb-scl-export.webp", "{{AUTHOR_LINKEDIN}}", '"codeRepository"'):
        if value not in home:
            errors.append(f"homepage template missing {value}")
    for value in ("{{INSTALLER_URL}}", "{{PORTABLE_URL}}", "{{CHECKSUMS_URL}}", "{{ARSAS_VERSION}}", "Download Center"):
        if value not in download:
            errors.append(f"download template missing {value}")
    for value in ("{{AUTHOR_LINKEDIN}}", "{{AUTHOR_GITHUB}}", "{{REPOSITORY_URL}}", "Download Center"):
        if value not in about:
            errors.append(f"about template missing {value}")
    for value in ('href="fat-testing.html"', 'href="sat-testing.html"', 'href="commissioning.html"', 'href="multi-vendor-integration.html"'):
        if value not in solutions:
            errors.append(f"solutions template missing {value}")
    for value in (
        'href="reporting-silent.html"', 'href="brcb-vs-urcb.html"', 'href="rcb-reserved.html"',
        'href="empty-dataset.html"', 'href="port-102-connection-failed.html"', 'href="comtrade-download.html"',
        'href="goose-sequence.html"', 'href="cid-rejected.html"', 'href="live-model-vs-scl.html"',
        'href="direct-vs-sbo.html"', 'href="commandtermination-addcause.html"',
    ):
        if value not in guides:
            errors.append(f"guides template missing {value}")

    root_html = sorted(path.name for path in LANDING.glob("*.html") if not VERIFICATION_FILE_PATTERN.fullmatch(path.name))
    if root_html:
        errors.append("legacy landing HTML remains outside templates: " + ", ".join(root_html))
    verification = [path.name for path in LANDING.glob("google*.html") if VERIFICATION_FILE_PATTERN.fullmatch(path.name)]
    if len(verification) > 1:
        errors.append("multiple Google verification HTML files are present")
    if (LANDING / "sitemap.xml").exists():
        errors.append("landing/sitemap.xml must be generated from site.json, not stored as a second source")

    for relative in (
        "assets/screenshots/arsas-first-launch.webp",
        "assets/screenshots/arsas-rcb-scl-export.webp",
        "assets/social-card.png", "site.webmanifest", "robots.txt",
    ):
        if not (LANDING / relative).exists():
            errors.append(f"missing landing source file: {relative}")
    if not APP_ICON_SOURCE.exists():
        errors.append("missing latest application icon: Assets/app-icon.png")
    else:
        try:
            if png_size(APP_ICON_SOURCE) != (512, 512):
                errors.append(f"Assets/app-icon.png must be 512x512, found {png_size(APP_ICON_SOURCE)}")
        except ValueError as exc:
            errors.append(f"Assets/app-icon.png: {exc}")


def main() -> int:
    errors: list[str] = []
    config = read_config(errors)
    templates = validate_registry(config, errors) if config else []
    validate_partials(errors)
    for template, content_type in templates:
        validate_template(template, content_type, errors)
    validate_contract(errors)
    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS product-source validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print(f"ARSAS product-source validation passed: {len(templates)} templates, 11 troubleshooting guides and latest 512px app icon.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
