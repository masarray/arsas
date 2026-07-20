#!/usr/bin/env python3
"""Build the ARSAS product website from templates, partials and product data."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import struct
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "landing"
TEMPLATES = SOURCE / "templates"
PARTIALS = SOURCE / "partials"
CONFIG_PATH = SOURCE / "site.json"
PROJECT_PATH = ROOT / "ArIED61850Tester.csproj"
APP_ICON_SOURCE = ROOT / "Assets" / "app-icon.png"
INCLUDE_PATTERN = re.compile(r"\{\{>\s*([a-z0-9-]+)\s*\}\}", re.IGNORECASE)
TOKEN_PATTERN = re.compile(r"\{\{([A-Z0-9_]+)\}\}")
VERIFICATION_PATTERN = re.compile(r"google[a-z0-9]+\.html", re.IGNORECASE)


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
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise SystemExit("ARSAS project Version must use major.minor.patch")
    return version


def read_config() -> dict[str, object]:
    try:
        config = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise SystemExit(f"Cannot read landing/site.json: {exc}") from exc
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


def token_values(config: dict[str, object], version: str) -> dict[str, str]:
    product = config["product"]
    author = config["author"]
    downloads = config["downloads"]
    assert isinstance(product, dict) and isinstance(author, dict) and isinstance(downloads, dict)
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


def write_build_info(output: Path, config: dict[str, object], version: str, pages: list[dict[str, object]]) -> None:
    product = config["product"]
    index_now = config["indexNow"]
    assert isinstance(product, dict) and isinstance(index_now, dict)
    languages = sorted({str(entry.get("language", "en")) for entry in pages if entry.get("index", True) is not False})
    payload = {
        "schemaVersion": 3,
        "product": product["name"],
        "version": version,
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


def build(output: Path) -> None:
    version = read_version()
    config = read_config()
    values = token_values(config, version)
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
    write_build_info(output, config, version, pages)

    required = {
        *generated, "site.json", "sitemap.xml", "build-info.json", str(index_now["keyFile"]),
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
    print(f"Built ARSAS product website {version} at {output} ({len(generated)} pages, languages en/id, favicon {icon_size}).")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default=str(ROOT / "_site"))
    args = parser.parse_args()
    build(Path(args.output).resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
