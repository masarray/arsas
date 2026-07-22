#!/usr/bin/env python3
"""Validate ARSAS product templates, localization, release trust and source assets."""

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
APP_ICON = ROOT / "Assets" / "app-icon.png"
INCLUDE = re.compile(r"\{\{>\s*([a-z0-9-]+)\s*\}\}", re.IGNORECASE)
TOKEN = re.compile(r"\{\{([A-Z0-9_]+)\}\}")
VERIFICATION = re.compile(r"google[a-z0-9]+\.html", re.IGNORECASE)
EXPECTED_NAV = {"overview", "capabilities", "solutions", "guides", "architecture", "about", "download"}
GUIDES = {
    "reporting-silent.html", "brcb-vs-urcb.html", "rcb-reserved.html", "empty-dataset.html",
    "port-102-connection-failed.html", "comtrade-download.html", "goose-sequence.html",
    "cid-rejected.html", "live-model-vs-scl.html", "direct-vs-sbo.html", "commandtermination-addcause.html",
}
PAIRS = {
    "": "id.html", "download.html": "unduh.html", "release-notes.html": "catatan-rilis.html",
    "quick-start.html": "panduan-mulai-arsas.html", "faq.html": "faq-arsas.html",
    "compatibility.html": "bukti-kompatibilitas.html", "demo.html": "demo-arsas.html",
    "guides.html": "panduan.html", "mms-client.html": "mms-client-iec61850.html",
    "smart-reporting.html": "smart-reporting-iec61850.html", "goose-analyzer.html": "analyzer-goose-iec61850.html",
    "file-transfer.html": "transfer-file-comtrade-iec61850.html", "scl-workspace.html": "workspace-scl-iec61850.html",
    "fat-testing.html": "pengujian-fat-iec61850.html", "sat-testing.html": "pengujian-sat-iec61850.html",
    "commissioning.html": "commissioning-iec61850.html", "multi-vendor-integration.html": "integrasi-multi-vendor-iec61850.html",
}
KNOWN_TOKENS = {
    "ARSAS_VERSION", "PRODUCT_NAME", "CANONICAL_ROOT", "REPOSITORY_URL", "ENGINE_REPOSITORY_URL",
    "AUTHOR_NAME", "AUTHOR_LINKEDIN", "AUTHOR_GITHUB", "INSTALLER_URL", "PORTABLE_URL", "CHECKSUMS_URL",
    "STABLE_VERSION", "STABLE_PUBLISHED_ISO", "STABLE_PUBLISHED_DATE", "STABLE_PUBLISHED_DATE_ID",
    "INSTALLER_SIZE", "PORTABLE_SIZE", "INSTALLER_SHA256", "PORTABLE_SHA256", "RELEASE_TITLE",
    "RELEASE_TITLE_ID", "RELEASE_SUMMARY", "RELEASE_SUMMARY_ID", "RELEASE_HIGHLIGHTS",
    "RELEASE_HIGHLIGHTS_ID", "RELEASE_IMPROVEMENTS", "RELEASE_IMPROVEMENTS_ID", "RELEASE_LIMITATIONS",
    "RELEASE_LIMITATIONS_ID", "SIGNING_STATUS", "SIGNING_LABEL", "SIGNING_LABEL_ID", "SIGNING_DETAIL",
    "SIGNING_DETAIL_ID", "RELEASE_SCREENSHOT_SRC", "RELEASE_SCREENSHOT_WIDTH", "RELEASE_SCREENSHOT_HEIGHT",
    "RELEASE_SCREENSHOT_ALT", "RELEASE_SCREENSHOT_ALT_ID", "RELEASE_SCREENSHOT_CAPTION",
    "RELEASE_SCREENSHOT_CAPTION_ID", "ISSUES_URL", "RELEASE_URL",
}


class HtmlAudit(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.lang = ""
        self.title = ""
        self.in_title = False
        self.h1 = 0
        self.description = ""
        self.meta: dict[str, str] = {}
        self.body_page = ""
        self.images: list[dict[str, str | None]] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "html": self.lang = values.get("lang") or ""
        if tag == "title": self.in_title = True
        if tag == "h1": self.h1 += 1
        if tag == "body": self.body_page = values.get("data-page") or ""
        if tag == "meta":
            key = (values.get("name") or values.get("property") or "").lower()
            value = values.get("content") or ""
            if key: self.meta[key] = value
            if key == "description": self.description = value
        if tag == "img": self.images.append(values)

    def handle_endtag(self, tag: str) -> None:
        if tag == "title": self.in_title = False

    def handle_data(self, data: str) -> None:
        if self.in_title: self.title += data


def expand(text: str, errors: list[str], stack: tuple[str, ...] = ()) -> str:
    def replacement(match: re.Match[str]) -> str:
        name = match.group(1).lower()
        if name in stack:
            errors.append("circular partial include: " + " -> ".join((*stack, name)))
            return ""
        path = PARTIALS / f"{name}.html"
        if not path.is_file():
            errors.append(f"missing partial {path.relative_to(ROOT)}")
            return ""
        return expand(path.read_text(encoding="utf-8"), errors, (*stack, name))
    previous = None
    while previous != text:
        previous = text
        text = INCLUDE.sub(replacement, text)
    return text


def png_size(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n": raise ValueError("not a PNG")
    return struct.unpack(">II", data[16:24])


def validate_release(errors: list[str]) -> None:
    try:
        evidence = json.loads((LANDING / "latest.json").read_text(encoding="utf-8"))
        notes = json.loads((LANDING / "release-notes.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"release JSON: {exc}")
        return
    version = str(evidence.get("version", ""))
    if evidence.get("schemaVersion") != 1 or evidence.get("product") != "ARSAS" or evidence.get("channel") != "stable":
        errors.append("latest.json stable identity is invalid")
    if not re.fullmatch(r"\d+\.\d+\.\d+", version) or notes.get("version") != version:
        errors.append("release evidence and notes versions differ")
    for key, filename in (("installer", "ARSAS-Windows-x64-Setup.exe"), ("portable", "ARSAS-Windows-x64-Portable.zip")):
        item = evidence.get(key)
        if not isinstance(item, dict) or item.get("name") != filename or not re.fullmatch(r"[0-9a-fA-F]{64}", str(item.get("sha256", ""))):
            errors.append(f"latest.json {key} evidence is invalid")
    if not isinstance(evidence.get("checksums"), dict): errors.append("latest.json checksum evidence is missing")
    signing = evidence.get("codeSigning")
    note_signing = notes.get("codeSigning")
    if not isinstance(signing, dict) or signing.get("status") not in {"signed", "unsigned"}: errors.append("code-signing status is invalid")
    if not isinstance(note_signing, dict) or note_signing.get("status") != (signing or {}).get("status"): errors.append("release signing statuses differ")
    screenshot = notes.get("screenshot")
    if not isinstance(screenshot, dict) or not (LANDING / str(screenshot.get("src", ""))).is_file(): errors.append("stable screenshot is missing")
    if notes.get("issuesUrl") != "https://github.com/masarray/arsas/issues/new/choose": errors.append("release issue URL is invalid")


def main() -> int:
    errors: list[str] = []
    try:
        config = json.loads((LANDING / "site.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"ARSAS product-source validation failed:\n- landing/site.json: {exc}", file=sys.stderr)
        return 1
    pages = config.get("pages")
    if not isinstance(pages, list): pages = []
    entries: dict[str, dict[str, object]] = {}
    templates: set[str] = set()
    for item in pages:
        if not isinstance(item, dict) or not isinstance(item.get("path"), str) or not isinstance(item.get("template"), str):
            errors.append("invalid page registry entry")
            continue
        path, template = str(item["path"]), str(item["template"])
        if path in entries or template in templates: errors.append(f"duplicate page or template {path or template}")
        entries[path] = item
        templates.add(template)
    if len(pages) != 54: errors.append(f"expected 54 registered pages, found {len(pages)}")
    if sum(item.get("contentType") == "guide" for item in entries.values()) != 11: errors.append("expected 11 troubleshooting guides")
    if sum(item.get("contentType") == "localized" for item in entries.values()) != 17: errors.append("expected 17 Indonesian pages")
    if entries.get("404.html", {}).get("index", True) is not False: errors.append("404.html must be excluded from sitemap")
    for english, indonesian in PAIRS.items():
        expected = {"en": english, "id": indonesian, "x-default": english}
        en, id_page = entries.get(english), entries.get(indonesian)
        if not isinstance(en, dict) or en.get("language") != "en" or en.get("alternates") != expected: errors.append(f"invalid English localization pair {english or 'index.html'}")
        if not isinstance(id_page, dict) or id_page.get("language") != "id" or id_page.get("contentType") != "localized" or id_page.get("alternates") != expected: errors.append(f"invalid Indonesian localization pair {indonesian}")
    actual_templates = {path.name for path in TEMPLATES.glob("*.html")}
    if actual_templates != templates: errors.append("template registry and disk content differ")

    for path, item in entries.items():
        template_path = TEMPLATES / str(item["template"])
        if not template_path.is_file():
            errors.append(f"missing template {template_path.name}")
            continue
        raw = template_path.read_text(encoding="utf-8")
        rendered = expand(raw, errors)
        audit = HtmlAudit(); audit.feed(rendered)
        label = template_path.name
        if not audit.title.strip() or len(audit.title.strip()) > 100: errors.append(f"{label}: invalid title")
        if audit.h1 != 1: errors.append(f"{label}: expected one h1")
        if not 60 <= len(audit.description) <= 260: errors.append(f"{label}: invalid description")
        if not audit.body_page: errors.append(f"{label}: missing body data-page")
        language = str(item.get("language", "en"))
        if audit.lang != language: errors.append(f"{label}: html lang must be {language}")
        for key in ("og:title", "og:description", "og:url", "og:image", "og:image:width", "og:image:height"):
            if not audit.meta.get(key): errors.append(f"{label}: missing {key}")
        for image in audit.images:
            src = image.get("src") or ""
            if image.get("alt") is None or not image.get("width") or not image.get("height"): errors.append(f"{label}: incomplete image metadata {src}")
            if src == "assets/app-icon.png": continue
            if "{{" not in src and not (LANDING / src).is_file(): errors.append(f"{label}: missing image {src}")
        unknown = set(TOKEN.findall(rendered)) - KNOWN_TOKENS
        if unknown: errors.append(f"{label}: unknown tokens {sorted(unknown)}")
        if '<meta name="keywords"' in rendered.lower(): errors.append(f"{label}: meta keywords are forbidden")
        if path != "404.html" and ("{{> header}}" not in raw or "{{> footer}}" not in raw): errors.append(f"{label}: missing shared chrome")
        if item.get("contentType") == "localized":
            if "{{> download-cta-id}}" not in raw or '"inLanguage":"id"' not in raw.replace(" ", "") or 'hreflang="en"' not in raw: errors.append(f"{label}: incomplete Indonesian contract")
        elif path not in {"", "download.html", "404.html"} and "{{> download-cta}}" not in raw: errors.append(f"{label}: missing shared download CTA")
        if item.get("contentType") == "guide" and ('"@type":"TechArticle"' not in raw.replace(" ", "") or "{{> guide-boundary}}" not in raw): errors.append(f"{label}: incomplete guide contract")

    header = (PARTIALS / "header.html").read_text(encoding="utf-8")
    footer = (PARTIALS / "footer.html").read_text(encoding="utf-8")
    for nav in EXPECTED_NAV:
        if f'data-nav-page="{nav}"' not in header: errors.append(f"header missing navigation {nav}")
    for value in ("quick-start.html", "faq.html", "compatibility.html", "demo.html", "privacy.html", "{{AUTHOR_LINKEDIN}}"):
        if value not in footer: errors.append(f"footer missing {value}")
    root_html = [path.name for path in LANDING.glob("*.html") if not VERIFICATION.fullmatch(path.name)]
    if root_html: errors.append("legacy HTML outside templates: " + ", ".join(sorted(root_html)))
    for required in ("device-evidence.json", "adoption.css", "guide-filter.js", "demo.js", "latest.json", "release-notes.json", "robots.txt"):
        if not (LANDING / required).is_file(): errors.append(f"missing landing source {required}")
    if not APP_ICON.is_file(): errors.append("missing Assets/app-icon.png")
    else:
        try:
            width, height = png_size(APP_ICON)
            if width != height or width < 256: errors.append(f"invalid app icon dimensions {width}x{height}")
        except ValueError as exc: errors.append(f"app icon: {exc}")
    validate_release(errors)

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS product-source validation failed:", file=sys.stderr)
        for error in errors: print(f"- {error}", file=sys.stderr)
        return 1
    width, height = png_size(APP_ICON)
    print(f"ARSAS product-source validation passed: 54 templates, 11 guides, 17 Indonesian pages, 17 hreflang pairs, stable release trust and {width}x{height} app icon.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
