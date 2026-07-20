#!/usr/bin/env python3
"""Validate product templates, identity and distribution contracts before build."""

from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LANDING = ROOT / "landing"
TEMPLATES = (LANDING / "templates" / "index.html", LANDING / "templates" / "download.html")
KNOWN_TOKENS = {
    "ARSAS_VERSION", "PRODUCT_NAME", "CANONICAL_ROOT", "REPOSITORY_URL",
    "ENGINE_REPOSITORY_URL", "AUTHOR_NAME", "AUTHOR_LINKEDIN", "AUTHOR_GITHUB",
    "INSTALLER_URL", "PORTABLE_URL", "CHECKSUMS_URL",
}


class Parser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.in_title = False
        self.title = ""
        self.h1 = 0
        self.description: str | None = None
        self.meta: dict[str, str] = {}
        self.images: list[dict[str, str | None]] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "title":
            self.in_title = True
        elif tag == "h1":
            self.h1 += 1
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


def validate_template(path: Path, errors: list[str]) -> None:
    if not path.exists():
        errors.append(f"missing template: {path.relative_to(ROOT)}")
        return
    text = path.read_text(encoding="utf-8")
    parser = Parser()
    parser.feed(text)
    label = path.relative_to(ROOT)

    if not parser.title.strip() or len(parser.title.strip()) > 75:
        errors.append(f"{label}: invalid title")
    if parser.h1 != 1:
        errors.append(f"{label}: expected one h1, found {parser.h1}")
    if not parser.description or not 70 <= len(parser.description) <= 220:
        errors.append(f"{label}: invalid meta description")
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


def validate_config(errors: list[str]) -> None:
    try:
        config = json.loads((LANDING / "site.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"landing/site.json: {exc}")
        return

    checks = {
        ("product", "name"): "ARSAS",
        ("product", "repository"): "https://github.com/masarray/arsas",
        ("author", "name"): "Ari Sulistiono",
        ("author", "linkedin"): "https://www.linkedin.com/in/ari-sulistiono",
        ("author", "github"): "https://github.com/masarray",
    }
    for path, expected in checks.items():
        value: object = config
        for key in path:
            value = value.get(key) if isinstance(value, dict) else None
        if value != expected:
            errors.append(f"landing/site.json: {'.'.join(path)} must be {expected}")

    for group, key in (("downloads", "installer"), ("downloads", "portable"), ("downloads", "checksums")):
        value = config.get(group, {}).get(key) if isinstance(config.get(group), dict) else None
        if not isinstance(value, str) or not value.startswith("https://github.com/masarray/arsas/releases/latest/download/"):
            errors.append(f"landing/site.json: invalid {group}.{key}")


def validate_contract(errors: list[str]) -> None:
    home = TEMPLATES[0].read_text(encoding="utf-8") if TEMPLATES[0].exists() else ""
    download = TEMPLATES[1].read_text(encoding="utf-8") if TEMPLATES[1].exists() else ""
    about_path = LANDING / "about.html"
    about = about_path.read_text(encoding="utf-8") if about_path.exists() else ""

    for value in ("{{INSTALLER_URL}}", 'href="download.html"', "arsas-rcb-scl-export.webp", "{{AUTHOR_LINKEDIN}}", '"codeRepository"'):
        if value not in home:
            errors.append(f"homepage template missing {value}")
    for value in ("{{INSTALLER_URL}}", "{{PORTABLE_URL}}", "{{CHECKSUMS_URL}}", "{{ARSAS_VERSION}}"):
        if value not in download:
            errors.append(f"download template missing {value}")
    for value in ("https://www.linkedin.com/in/ari-sulistiono", "https://github.com/masarray"):
        if value not in about:
            errors.append(f"about page missing {value}")
    if "github.com/masarray/arsas#quick-start" in home + download:
        errors.append("primary product templates route users to README quick-start")

    required_files = (
        "assets/screenshots/arsas-first-launch.webp",
        "assets/screenshots/arsas-rcb-scl-export.webp",
        "assets/social-card.png",
        "about.html",
        "site.webmanifest",
        "robots.txt",
    )
    for relative in required_files:
        if not (LANDING / relative).exists():
            errors.append(f"missing landing source file: {relative}")


def validate_sitemap(errors: list[str]) -> None:
    try:
        ET.parse(LANDING / "sitemap.xml")
    except (OSError, ET.ParseError) as exc:
        errors.append(f"landing/sitemap.xml: {exc}")
        return
    sitemap = (LANDING / "sitemap.xml").read_text(encoding="utf-8")
    if "https://masarray.github.io/arsas/about.html" not in sitemap:
        errors.append("landing/sitemap.xml missing about.html")


def main() -> int:
    errors: list[str] = []
    validate_config(errors)
    for template in TEMPLATES:
        validate_template(template, errors)
    validate_contract(errors)
    validate_sitemap(errors)
    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS product-source validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print("ARSAS product-source validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
