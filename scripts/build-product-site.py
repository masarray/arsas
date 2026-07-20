#!/usr/bin/env python3
"""Build the ARSAS product website from explicit templates and product data."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "landing"
TEMPLATES = SOURCE / "templates"
CONFIG_PATH = SOURCE / "site.json"
PROJECT_PATH = ROOT / "ArIED61850Tester.csproj"
APP_ICON_SOURCE = ROOT / "Assets" / "app-icon-256.png"

REMOTE_SCREENSHOTS = {
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%281%29.webp": "assets/screenshots/arsas-first-launch.webp",
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%282%29.webp": "assets/screenshots/arsas-multi-ied.webp",
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%283%29.webp": "assets/screenshots/arsas-live-values.webp",
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%284%29.webp": "assets/screenshots/arsas-event-log.webp",
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%285%29.webp": "assets/screenshots/arsas-goose.webp",
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%286%29.webp": "assets/screenshots/arsas-diagnostics.webp",
}


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
        "product.name",
        "product.canonicalRoot",
        "product.repository",
        "author.name",
        "author.linkedin",
        "author.github",
        "downloads.installer",
        "downloads.portable",
        "downloads.checksums",
    )
    for dotted in required:
        value: object = config
        for key in dotted.split("."):
            if not isinstance(value, dict) or key not in value:
                raise SystemExit(f"landing/site.json is missing {dotted}")
            value = value[key]
        if not isinstance(value, str) or not value.strip():
            raise SystemExit(f"landing/site.json has invalid {dotted}")
    return config


def token_values(config: dict[str, object], version: str) -> dict[str, str]:
    product = config["product"]
    author = config["author"]
    downloads = config["downloads"]
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


def render(text: str, values: dict[str, str]) -> str:
    for key, value in values.items():
        text = text.replace("{{" + key + "}}", value)
    unresolved = sorted(set(re.findall(r"\{\{([A-Z0-9_]+)\}\}", text)))
    if unresolved:
        raise SystemExit("Unresolved landing template tokens: " + ", ".join(unresolved))
    return text


def normalize_legacy_page(text: str, version: str) -> str:
    """Only normalize assets and version on pages not migrated to templates yet."""
    for remote, local in REMOTE_SCREENSHOTS.items():
        text = text.replace(remote, local)
    text = text.replace(
        "https://masarray.github.io/arsas/assets/social-card.svg",
        "https://masarray.github.io/arsas/assets/social-card.png",
    )
    text = text.replace(
        '<link rel="preconnect" href="https://raw.githubusercontent.com" crossorigin />',
        "",
    )
    text = re.sub(
        r'<link\s+rel="preload"\s+as="image"\s+href="https://raw\.githubusercontent\.com/[^\"]+"\s+fetchpriority="high"\s*/?>',
        '<link rel="preload" as="image" href="assets/screenshots/arsas-first-launch.webp" fetchpriority="high" />',
        text,
        flags=re.IGNORECASE,
    )
    text = re.sub(
        r'<link\s+rel="icon"\s+href="assets/favicon\.svg"\s+type="image/svg\+xml"\s*/?>',
        '<link rel="icon" href="assets/app-icon.png" type="image/png" sizes="256x256" />\n  <link rel="apple-touch-icon" href="assets/app-icon.png" />',
        text,
        flags=re.IGNORECASE,
    )
    return re.sub(
        r'("softwareVersion"\s*:\s*")\d+\.\d+\.\d+(\")',
        rf"\g<1>{version}\g<2>",
        text,
    )


def install_icon(output: Path) -> None:
    if not APP_ICON_SOURCE.exists():
        raise SystemExit(f"Missing application icon: {APP_ICON_SOURCE}")
    destination = output / "assets" / "app-icon.png"
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(APP_ICON_SOURCE, destination)

    manifest_path = output / "site.webmanifest"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["icons"] = [{
        "src": "assets/app-icon.png",
        "sizes": "256x256",
        "type": "image/png",
        "purpose": "any",
    }]
    manifest_path.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def write_build_info(output: Path, config: dict[str, object], version: str) -> None:
    payload = {
        "schemaVersion": 1,
        "product": config["product"]["name"],
        "version": version,
        "canonicalRoot": config["product"]["canonicalRoot"],
        "repository": config["product"]["repository"],
        "author": config["author"],
    }
    (output / "build-info.json").write_text(
        json.dumps(payload, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def build(output: Path) -> None:
    version = read_version()
    config = read_config()
    values = token_values(config, version)

    if output.exists():
        shutil.rmtree(output)
    shutil.copytree(SOURCE, output)
    shutil.rmtree(output / "templates", ignore_errors=True)

    migrated = {"index.html", "download.html"}
    for template in TEMPLATES.glob("*.html"):
        target = output / template.name
        target.write_text(render(template.read_text(encoding="utf-8"), values), encoding="utf-8")

    for page in output.glob("*.html"):
        if page.name in migrated:
            continue
        page.write_text(normalize_legacy_page(page.read_text(encoding="utf-8"), version), encoding="utf-8")

    install_icon(output)
    write_build_info(output, config, version)

    required = (
        "index.html",
        "download.html",
        "about.html",
        "site.json",
        "build-info.json",
        "assets/app-icon.png",
        "assets/social-card.png",
        "assets/screenshots/arsas-first-launch.webp",
        "assets/screenshots/arsas-rcb-scl-export.webp",
    )
    missing = [item for item in required if not (output / item).exists()]
    if missing:
        raise SystemExit("Missing product-site output: " + ", ".join(missing))

    combined = "\n".join(page.read_text(encoding="utf-8") for page in output.glob("*.html"))
    forbidden = (
        "raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot",
        "https://masarray.github.io/arsas/assets/social-card.svg",
        'href="assets/favicon.svg"',
        "{{ARSAS_",
        "{{AUTHOR_",
    )
    for value in forbidden:
        if value in combined:
            raise SystemExit(f"Deployable product site still contains forbidden value: {value}")

    print(f"Built ARSAS product website {version} at {output}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default=str(ROOT / "_site"))
    args = parser.parse_args()
    build(Path(args.output).resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
