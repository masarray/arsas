#!/usr/bin/env python3
"""Build the public ARSAS website with local media and deterministic metadata."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SITE_SOURCE = ROOT / "landing"
SITE_CONFIG = SITE_SOURCE / "site.json"
PROJECT_FILE = ROOT / "ArIED61850Tester.csproj"

SCREENSHOT_MAP = {
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%281%29.webp": "assets/screenshots/arsas-first-launch.webp",
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%282%29.webp": "assets/screenshots/arsas-multi-ied.webp",
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%283%29.webp": "assets/screenshots/arsas-live-values.webp",
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%284%29.webp": "assets/screenshots/arsas-event-log.webp",
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%285%29.webp": "assets/screenshots/arsas-goose.webp",
    "https://raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot/arsas%20%286%29.webp": "assets/screenshots/arsas-diagnostics.webp",
}

SOCIAL_SVG = "https://masarray.github.io/arsas/assets/social-card.svg"
SOCIAL_PNG = "https://masarray.github.io/arsas/assets/social-card.png"
APP_ICON_SOURCE = ROOT / "Assets" / "app-icon-256.png"
APP_ICON_RELATIVE = Path("assets/app-icon.png")
VERSION_TOKEN = "{{ARSAS_VERSION}}"


def read_project_version() -> str:
    try:
        root = ET.parse(PROJECT_FILE).getroot()
    except (OSError, ET.ParseError) as exc:
        raise SystemExit(f"Unable to read ARSAS project version: {exc}") from exc

    version = root.findtext(".//Version")
    if not version or not re.fullmatch(r"\d+\.\d+\.\d+", version.strip()):
        raise SystemExit("ArIED61850Tester.csproj must contain a semantic Version value")
    return version.strip()


def read_site_config() -> dict[str, object]:
    try:
        config = json.loads(SITE_CONFIG.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise SystemExit(f"Unable to read landing/site.json: {exc}") from exc

    for path in (
        ("product", "name"),
        ("product", "canonicalRoot"),
        ("product", "repository"),
        ("author", "name"),
        ("author", "linkedin"),
        ("author", "github"),
        ("downloads", "installer"),
        ("downloads", "portable"),
        ("downloads", "checksums"),
    ):
        value: object = config
        for key in path:
            if not isinstance(value, dict) or key not in value:
                raise SystemExit(f"landing/site.json is missing {'.'.join(path)}")
            value = value[key]
        if not isinstance(value, str) or not value.strip():
            raise SystemExit(f"landing/site.json has an invalid {'.'.join(path)}")
    return config


def rewrite_public_html(text: str, version: str) -> str:
    """Apply only deployment-safe asset and metadata normalization.

    Product navigation, download routes, repository links and CTA meaning are
    authored in source HTML and are intentionally not changed at build time.
    """

    for remote, local in SCREENSHOT_MAP.items():
        text = text.replace(remote, local)

    text = text.replace(SOCIAL_SVG, SOCIAL_PNG)
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

    text = text.replace(VERSION_TOKEN, version)
    text = re.sub(
        r'("softwareVersion"\s*:\s*")\d+\.\d+\.\d+(\")',
        rf"\g<1>{version}\g<2>",
        text,
    )
    return text


def install_app_icon(output: Path) -> None:
    if not APP_ICON_SOURCE.exists():
        raise SystemExit(f"ARSAS app icon was not found: {APP_ICON_SOURCE}")

    destination = output / APP_ICON_RELATIVE
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(APP_ICON_SOURCE, destination)

    manifest_path = output / "site.webmanifest"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["icons"] = [
        {
            "src": APP_ICON_RELATIVE.as_posix(),
            "sizes": "256x256",
            "type": "image/png",
            "purpose": "any",
        }
    ]
    manifest_path.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def write_build_info(output: Path, version: str, config: dict[str, object]) -> None:
    product = config["product"]
    author = config["author"]
    payload = {
        "schemaVersion": 1,
        "product": product["name"],
        "version": version,
        "canonicalRoot": product["canonicalRoot"],
        "repository": product["repository"],
        "author": {
            "name": author["name"],
            "linkedin": author["linkedin"],
            "github": author["github"],
        },
    }
    (output / "build-info.json").write_text(
        json.dumps(payload, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def build(source: Path, output: Path) -> None:
    version = read_project_version()
    config = read_site_config()

    if output.exists():
        shutil.rmtree(output)
    shutil.copytree(source, output)
    install_app_icon(output)

    transformed_pages = 0
    for page in output.rglob("*.html"):
        text = page.read_text(encoding="utf-8")
        rewritten = rewrite_public_html(text, version)
        if rewritten != text:
            transformed_pages += 1
            page.write_text(rewritten, encoding="utf-8")

    write_build_info(output, version, config)

    required = [output / path for path in (
        "assets/app-icon.png",
        "assets/social-card.png",
        "assets/screenshots/arsas-first-launch.webp",
        "assets/screenshots/arsas-multi-ied.webp",
        "assets/screenshots/arsas-live-values.webp",
        "assets/screenshots/arsas-event-log.webp",
        "assets/screenshots/arsas-goose.webp",
        "assets/screenshots/arsas-diagnostics.webp",
        "assets/screenshots/arsas-rcb-scl-export.webp",
        "assets/screenshots/arsas-overview-v1.6.19.webp",
        "index.html",
        "download.html",
        "about.html",
        "site.json",
        "build-info.json",
    )]
    missing = [str(path.relative_to(output)) for path in required if not path.exists()]
    if missing:
        raise SystemExit("Missing deployable landing assets: " + ", ".join(missing))

    deployed_text = "\n".join(path.read_text(encoding="utf-8") for path in output.rglob("*.html"))
    if "raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot" in deployed_text:
        raise SystemExit("Remote screenshot URL remains in deployable landing artifact")
    if SOCIAL_SVG in deployed_text:
        raise SystemExit("SVG social card remains referenced in deployable landing artifact")
    if "assets/favicon.svg" in deployed_text:
        raise SystemExit("Legacy SVG favicon remains referenced in deployable landing artifact")
    if VERSION_TOKEN in deployed_text:
        raise SystemExit("Unresolved ARSAS version token remains in deployable HTML")

    print(
        f"Built public ARSAS website at {output} "
        f"for version {version} ({transformed_pages} transformed pages)."
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default=str(SITE_SOURCE))
    parser.add_argument("--output", default=str(ROOT / "_site"))
    args = parser.parse_args()
    build(Path(args.source).resolve(), Path(args.output).resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
