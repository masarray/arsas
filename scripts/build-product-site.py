#!/usr/bin/env python3
"""Build the ARSAS product website from templates, product data and verified release evidence."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import struct
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "landing"
TEMPLATES = SOURCE / "templates"
PARTIALS = SOURCE / "partials"
CONFIG_PATH = SOURCE / "site.json"
RELEASE_NOTES_PATH = SOURCE / "release-notes.json"
DEFAULT_RELEASE_EVIDENCE_PATH = SOURCE / "latest.json"
PROJECT_PATH = ROOT / "ArIED61850Tester.csproj"
APP_ICON_SOURCE = ROOT / "Assets" / "app-icon.png"
INCLUDE_PATTERN = re.compile(r"\{\{>\s*([a-z0-9-]+)\s*\}\}", re.IGNORECASE)
TOKEN_PATTERN = re.compile(r"\{\{([A-Z0-9_]+)\}\}")
VERIFICATION_PATTERN = re.compile(r"google[a-z0-9]+\.html", re.IGNORECASE)
SEMVER_PATTERN = re.compile(r"\d+\.\d+\.\d+")
SHA256_PATTERN = re.compile(r"[0-9a-fA-F]{64}")


def read_json(path: Path, label: str) -> dict[str, object]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise SystemExit(f"Cannot read {label}: {exc}") from exc
    if not isinstance(value, dict):
        raise SystemExit(f"{label} must contain a JSON object")
    return value


def png_size(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"Application icon is not a valid PNG: {path}")
    return struct.unpack(">II", data[16:24])


def icon_dimensions() -> tuple[int, int]:
    if not APP_ICON_SOURCE.exists():
        raise SystemExit(f"Missing application icon: {APP_ICON_SOURCE}")
    width, height = png_size(APP_ICON_SOURCE)
    if width != height or width < 256:
        raise SystemExit(f"Application icon must be square and at least 256px, found {width}x{height}")
    return width, height


def read_version() -> str:
    try:
        project = ET.parse(PROJECT_PATH).getroot()
    except (OSError, ET.ParseError) as exc:
        raise SystemExit(f"Cannot read ARSAS project version: {exc}") from exc
    version = (project.findtext(".//Version") or "").strip()
    if not SEMVER_PATTERN.fullmatch(version):
        raise SystemExit("ARSAS project Version must use major.minor.patch")
    return version


def read_config() -> dict[str, object]:
    config = read_json(CONFIG_PATH, "landing/site.json")
    required = (
        "product.name", "product.canonicalRoot", "product.repository", "product.engineRepository",
        "author.name", "author.linkedin", "author.github", "downloads.installer",
        "downloads.portable", "downloads.checksums", "indexNow.endpoint", "indexNow.key",
        "indexNow.keyFile",
    )
    for dotted in required:
        value: object = config
        for key in dotted.split("."):
            if not isinstance(value, dict) or key not in value:
                raise SystemExit(f"landing/site.json is missing {dotted}")
            value = value[key]
        if not isinstance(value, str) or not value.strip():
            raise SystemExit(f"landing/site.json has invalid {dotted}")
    if not isinstance(config.get("pages"), list) or not config["pages"]:
        raise SystemExit("landing/site.json must define a non-empty pages registry")
    index_now = config["indexNow"]
    assert isinstance(index_now, dict)
    key_file = SOURCE / str(index_now["keyFile"])
    if not key_file.exists() or key_file.read_text(encoding="utf-8").strip() != index_now["key"]:
        raise SystemExit("IndexNow key metadata does not match the hosted key file")
    return config


def require_text(value: object, label: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise SystemExit(f"Invalid release field: {label}")
    return value.strip()


def require_list(value: object, label: str, minimum: int = 1) -> list[str]:
    if not isinstance(value, list) or len(value) < minimum or not all(isinstance(item, str) and item.strip() for item in value):
        raise SystemExit(f"Invalid release list: {label}")
    return [item.strip() for item in value]


def read_release_data(path: Path) -> tuple[dict[str, object], dict[str, object]]:
    evidence = read_json(path, f"stable release evidence {path}")
    notes = read_json(RELEASE_NOTES_PATH, "landing/release-notes.json")
    if evidence.get("schemaVersion") != 1 or evidence.get("product") != "ARSAS":
        raise SystemExit("Stable release evidence has invalid identity")
    version = require_text(evidence.get("version"), "version")
    if not SEMVER_PATTERN.fullmatch(version) or notes.get("version") != version:
        raise SystemExit("Release evidence and release notes versions must match")
    if evidence.get("channel") != "stable":
        raise SystemExit("Only stable release evidence may build the public Download Center")
    require_text(evidence.get("publishedAtUtc"), "publishedAtUtc")
    require_text(evidence.get("sourceCommit"), "sourceCommit")
    for key, expected_name in (
        ("installer", "ARSAS-Windows-x64-Setup.exe"),
        ("portable", "ARSAS-Windows-x64-Portable.zip"),
    ):
        package = evidence.get(key)
        if not isinstance(package, dict) or package.get("name") != expected_name:
            raise SystemExit(f"Stable release evidence has invalid {key} identity")
        if not SHA256_PATTERN.fullmatch(str(package.get("sha256", ""))):
            raise SystemExit(f"Stable release evidence has invalid {key} SHA-256")
        if not 1_000_000 <= int(package.get("sizeBytes", 0)) <= 500_000_000:
            raise SystemExit(f"Stable release evidence has invalid {key} size")
        require_text(package.get("url"), f"{key}.url")
    checksums = evidence.get("checksums")
    if not isinstance(checksums, dict):
        raise SystemExit("Stable release evidence is missing checksum asset")
    require_text(checksums.get("url"), "checksums.url")
    signing = evidence.get("codeSigning")
    if not isinstance(signing, dict) or signing.get("status") not in {"signed", "unsigned"}:
        raise SystemExit("Stable release evidence must declare code-signing status")

    for key in ("title", "titleId", "summary", "summaryId", "issuesUrl", "releaseUrl"):
        require_text(notes.get(key), key)
    for key in ("highlights", "highlightsId", "improvements", "improvementsId", "knownLimitations", "knownLimitationsId"):
        require_list(notes.get(key), key, 4)
    note_signing = notes.get("codeSigning")
    if not isinstance(note_signing, dict) or note_signing.get("status") != signing.get("status"):
        raise SystemExit("Release notes code-signing status must match release evidence")
    for key in ("label", "labelId", "detail", "detailId"):
        require_text(note_signing.get(key), f"codeSigning.{key}")
    screenshot = notes.get("screenshot")
    if not isinstance(screenshot, dict):
        raise SystemExit("Release notes screenshot metadata is missing")
    for key in ("src", "alt", "altId", "caption", "captionId"):
        require_text(screenshot.get(key), f"screenshot.{key}")
    if not (SOURCE / str(screenshot["src"])).exists():
        raise SystemExit("Release notes screenshot source does not exist")
    return evidence, notes


def human_size(size_bytes: int) -> str:
    return f"{size_bytes / (1024 * 1024):.1f} MiB"


def release_dates(value: str) -> tuple[str, str, str]:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise SystemExit(f"Invalid stable release publish date: {value}") from exc
    month_id = (
        "Januari", "Februari", "Maret", "April", "Mei", "Juni",
        "Juli", "Agustus", "September", "Oktober", "November", "Desember",
    )
    english = f"{parsed.day} {parsed.strftime('%B')} {parsed.year}"
    indonesian = f"{parsed.day} {month_id[parsed.month - 1]} {parsed.year}"
    return value, english, indonesian


def list_html(items: list[str]) -> str:
    return "".join(f"<li>{escape(item)}</li>" for item in items)


def token_values(
    config: dict[str, object],
    version: str,
    evidence: dict[str, object],
    notes: dict[str, object],
) -> dict[str, str]:
    product = config["product"]
    author = config["author"]
    downloads = config["downloads"]
    installer = evidence["installer"]
    portable = evidence["portable"]
    signing = notes["codeSigning"]
    screenshot = notes["screenshot"]
    assert isinstance(product, dict) and isinstance(author, dict) and isinstance(downloads, dict)
    assert isinstance(installer, dict) and isinstance(portable, dict)
    assert isinstance(signing, dict) and isinstance(screenshot, dict)
    published_iso, published_en, published_id = release_dates(str(evidence["publishedAtUtc"]))
    return {
        "ARSAS_VERSION": version,
        "PRODUCT_NAME": str(product["name"]),
        "CANONICAL_ROOT": str(product["canonicalRoot"]),
        "REPOSITORY_URL": str(product["repository"]),
        "ENGINE_REPOSITORY_URL": str(product["engineRepository"]),
        "AUTHOR_NAME": str(author["name"]),
        "AUTHOR_LINKEDIN": str(author["linkedin"]),
        "AUTHOR_GITHUB": str(author["github"]),
        "INSTALLER_URL": str(downloads["installer"]),
        "PORTABLE_URL": str(downloads["portable"]),
        "CHECKSUMS_URL": str(downloads["checksums"]),
        "STABLE_VERSION": str(evidence["version"]),
        "STABLE_PUBLISHED_ISO": published_iso,
        "STABLE_PUBLISHED_DATE": published_en,
        "STABLE_PUBLISHED_DATE_ID": published_id,
        "INSTALLER_SIZE": human_size(int(installer["sizeBytes"])),
        "PORTABLE_SIZE": human_size(int(portable["sizeBytes"])),
        "INSTALLER_SHA256": str(installer["sha256"]),
        "PORTABLE_SHA256": str(portable["sha256"]),
        "RELEASE_TITLE": str(notes["title"]),
        "RELEASE_TITLE_ID": str(notes["titleId"]),
        "RELEASE_SUMMARY": str(notes["summary"]),
        "RELEASE_SUMMARY_ID": str(notes["summaryId"]),
        "RELEASE_HIGHLIGHTS": list_html(require_list(notes["highlights"], "highlights")),
        "RELEASE_HIGHLIGHTS_ID": list_html(require_list(notes["highlightsId"], "highlightsId")),
        "RELEASE_IMPROVEMENTS": list_html(require_list(notes["improvements"], "improvements")),
        "RELEASE_IMPROVEMENTS_ID": list_html(require_list(notes["improvementsId"], "improvementsId")),
        "RELEASE_LIMITATIONS": list_html(require_list(notes["knownLimitations"], "knownLimitations")),
        "RELEASE_LIMITATIONS_ID": list_html(require_list(notes["knownLimitationsId"], "knownLimitationsId")),
        "SIGNING_STATUS": str(signing["status"]),
        "SIGNING_LABEL": str(signing["label"]),
        "SIGNING_LABEL_ID": str(signing["labelId"]),
        "SIGNING_DETAIL": str(signing["detail"]),
        "SIGNING_DETAIL_ID": str(signing["detailId"]),
        "RELEASE_SCREENSHOT_SRC": str(screenshot["src"]),
        "RELEASE_SCREENSHOT_WIDTH": str(screenshot["width"]),
        "RELEASE_SCREENSHOT_HEIGHT": str(screenshot["height"]),
        "RELEASE_SCREENSHOT_ALT": str(screenshot["alt"]),
        "RELEASE_SCREENSHOT_ALT_ID": str(screenshot["altId"]),
        "RELEASE_SCREENSHOT_CAPTION": str(screenshot["caption"]),
        "RELEASE_SCREENSHOT_CAPTION_ID": str(screenshot["captionId"]),
        "ISSUES_URL": str(notes["issuesUrl"]),
        "RELEASE_URL": str(notes["releaseUrl"]),
    }


def expand_partials(text: str, stack: tuple[str, ...] = ()) -> str:
    def replace(match: re.Match[str]) -> str:
        name = match.group(1).lower()
        if name in stack:
            raise SystemExit("Circular landing partial include: " + " -> ".join((*stack, name)))
        path = PARTIALS / f"{name}.html"
        if not path.exists():
            raise SystemExit(f"Missing landing partial: {path.relative_to(ROOT)}")
        return expand_partials(path.read_text(encoding="utf-8"), (*stack, name))

    previous = None
    while previous != text:
        previous = text
        text = INCLUDE_PATTERN.sub(replace, text)
    return text


def render(text: str, values: dict[str, str], icon_size: str) -> str:
    text = expand_partials(text)
    for key, value in values.items():
        text = text.replace("{{" + key + "}}", value)
    text = re.sub(
        r'href="assets/app-icon\.png" type="image/png" sizes="[0-9]+x[0-9]+"',
        f'href="assets/app-icon.png" type="image/png" sizes="{icon_size}"',
        text,
    )
    width, height = icon_size.split("x", 1)
    text = re.sub(
        r'src="assets/app-icon\.png" width="[0-9]+" height="[0-9]+"',
        f'src="assets/app-icon.png" width="{width}" height="{height}"',
        text,
    )
    unresolved = sorted(set(TOKEN_PATTERN.findall(text)))
    if unresolved:
        raise SystemExit("Unresolved landing template tokens: " + ", ".join(unresolved))
    if INCLUDE_PATTERN.search(text):
        raise SystemExit("Unresolved landing partial include remains")
    return text


def page_registry(config: dict[str, object]) -> list[dict[str, object]]:
    pages = config.get("pages")
    if not isinstance(pages, list):
        raise SystemExit("landing/site.json pages registry is invalid")
    normalized: list[dict[str, object]] = []
    paths: set[str] = set()
    templates: set[str] = set()
    for entry in pages:
        if not isinstance(entry, dict):
            raise SystemExit("Every landing page registry entry must be an object")
        path, template = entry.get("path"), entry.get("template")
        if not isinstance(path, str) or not isinstance(template, str):
            raise SystemExit("Every landing page registry entry needs string path and template")
        if path in paths or template in templates:
            raise SystemExit(f"Duplicate landing page registry entry: {path or template}")
        if path not in ("", "404.html") and not path.endswith(".html"):
            raise SystemExit(f"Invalid landing page path: {path}")
        if not template.endswith(".html") or not (TEMPLATES / template).exists():
            raise SystemExit(f"Registered landing template is missing: {template}")
        alternates = entry.get("alternates")
        if alternates is not None:
            if not isinstance(alternates, dict) or set(alternates) != {"en", "id", "x-default"}:
                raise SystemExit(f"Localized page has invalid alternates: {path or 'index.html'}")
            if path not in alternates.values():
                raise SystemExit(f"Localized page alternates must reference itself: {path or 'index.html'}")
        paths.add(path)
        templates.add(template)
        normalized.append(entry)
    return normalized


def absolute_url(root: str, path: str) -> str:
    return root if path == "" else root + path


def inject_alternate_links(text: str, entry: dict[str, object], root: str) -> str:
    alternates = entry.get("alternates")
    if not isinstance(alternates, dict):
        return text
    links = "\n".join(
        f'  <link rel="alternate" hreflang="{language}" href="{escape(absolute_url(root, str(alternates[language])))}" />'
        for language in ("en", "id", "x-default")
    )
    if "</head>" not in text:
        raise SystemExit(f"Cannot inject alternate-language links into {entry.get('template')}")
    return text.replace("</head>", links + "\n</head>", 1)


def install_icon(output: Path, icon_size: str) -> None:
    destination = output / "assets" / "app-icon.png"
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(APP_ICON_SOURCE, destination)
    manifest_path = output / "site.webmanifest"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["icons"] = [{"src": "assets/app-icon.png", "sizes": icon_size, "type": "image/png", "purpose": "any maskable"}]
    manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def write_sitemap(output: Path, config: dict[str, object], pages: list[dict[str, object]]) -> None:
    product = config["product"]
    assert isinstance(product, dict)
    root = str(product["canonicalRoot"])
    has_alternates = any(isinstance(entry.get("alternates"), dict) for entry in pages)
    namespace = ' xmlns:xhtml="http://www.w3.org/1999/xhtml"' if has_alternates else ""
    lines = ['<?xml version="1.0" encoding="UTF-8"?>', f'<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"{namespace}>']
    for entry in pages:
        if entry.get("index", True) is False:
            continue
        path = str(entry["path"])
        lines.extend(["  <url>", f"    <loc>{escape(absolute_url(root, path))}</loc>"])
        alternates = entry.get("alternates")
        if isinstance(alternates, dict):
            for language in ("en", "id", "x-default"):
                alternate_path = str(alternates[language])
                lines.append(f'    <xhtml:link rel="alternate" hreflang="{language}" href="{escape(absolute_url(root, alternate_path))}" />')
        lines.extend([
            f"    <lastmod>{escape(str(entry.get('lastmod', '2026-07-20')))}</lastmod>",
            f"    <changefreq>{escape(str(entry.get('changefreq', 'monthly')))}</changefreq>",
            f"    <priority>{escape(str(entry.get('priority', '0.7')))}</priority>",
            "  </url>",
        ])
    lines.append("</urlset>")
    (output / "sitemap.xml").write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_build_info(
    output: Path,
    config: dict[str, object],
    version: str,
    stable_version: str,
    pages: list[dict[str, object]],
) -> None:
    product = config["product"]
    index_now = config["indexNow"]
    assert isinstance(product, dict) and isinstance(index_now, dict)
    languages = sorted({str(entry.get("language", "en")) for entry in pages if entry.get("index", True) is not False})
    payload = {
        "schemaVersion": 3,
        "product": product["name"],
        "version": version,
        "stableReleaseVersion": stable_version,
        "canonicalRoot": product["canonicalRoot"],
        "repository": product["repository"],
        "author": config["author"],
        "languages": languages,
        "indexNowKeyLocation": str(product["canonicalRoot"]) + str(index_now["keyFile"]),
        "pages": [entry["path"] or "index.html" for entry in pages],
    }
    (output / "build-info.json").write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def legacy_html_names() -> list[str]:
    return sorted(path.name for path in SOURCE.glob("*.html") if not VERIFICATION_PATTERN.fullmatch(path.name))


def build(output: Path, release_evidence_path: Path) -> None:
    version = read_version()
    config = read_config()
    evidence, notes = read_release_data(release_evidence_path)
    values = token_values(config, version, evidence, notes)
    pages = page_registry(config)
    width, height = icon_dimensions()
    icon_size = f"{width}x{height}"
    product = config["product"]
    index_now = config["indexNow"]
    assert isinstance(product, dict) and isinstance(index_now, dict)
    root = str(product["canonicalRoot"])

    if output.exists():
        shutil.rmtree(output)
    shutil.copytree(SOURCE, output)
    shutil.rmtree(output / "templates", ignore_errors=True)
    shutil.rmtree(output / "partials", ignore_errors=True)
    (output / "latest.json").write_text(json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    generated: set[str] = set()
    for entry in pages:
        target_name = "index.html" if str(entry["path"]) == "" else str(entry["path"])
        target = output / target_name
        target.parent.mkdir(parents=True, exist_ok=True)
        source_text = (TEMPLATES / str(entry["template"])).read_text(encoding="utf-8")
        rendered = render(source_text, values, icon_size)
        rendered = inject_alternate_links(rendered, entry, root)
        target.write_text(rendered, encoding="utf-8")
        generated.add(target_name)

    source_html = legacy_html_names()
    if source_html:
        raise SystemExit("Legacy landing HTML remains outside templates: " + ", ".join(source_html))

    install_icon(output, icon_size)
    write_sitemap(output, config, pages)
    write_build_info(output, config, version, str(evidence["version"]), pages)

    required = {
        *generated, "site.json", "latest.json", "release-notes.json", "sitemap.xml", "build-info.json", str(index_now["keyFile"]),
        "assets/app-icon.png", "assets/social-card.png",
        "assets/screenshots/arsas-first-launch.webp", "assets/screenshots/arsas-multi-ied.webp",
        "assets/screenshots/arsas-live-values.webp", "assets/screenshots/arsas-event-log.webp",
        "assets/screenshots/arsas-goose.webp", "assets/screenshots/arsas-diagnostics.webp",
        "assets/screenshots/arsas-rcb-scl-export.webp",
    }
    missing = sorted(item for item in required if not (output / item).exists())
    if missing:
        raise SystemExit("Missing product-site output: " + ", ".join(missing))

    combined = "\n".join((output / page).read_text(encoding="utf-8") for page in sorted(generated))
    for value in (
        "raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot",
        "https://masarray.github.io/arsas/assets/social-card.svg", 'href="assets/favicon.svg"',
        "{{", "github.com/masarray/arsas#quick-start", '<meta name="keywords"',
    ):
        if value in combined:
            raise SystemExit(f"Deployable product site still contains forbidden value: {value}")
    print(
        f"Built ARSAS product website {version} with stable release {evidence['version']} at {output} "
        f"({len(generated)} pages, languages en/id, favicon {icon_size})."
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default=str(ROOT / "_site"))
    parser.add_argument("--release-evidence", default=str(DEFAULT_RELEASE_EVIDENCE_PATH))
    args = parser.parse_args()
    build(Path(args.output).resolve(), Path(args.release_evidence).resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
