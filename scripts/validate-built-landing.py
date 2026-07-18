#!/usr/bin/env python3
"""Validate the final public ARSAS website artifact."""

from __future__ import annotations

import json
import re
import struct
import sys
from html.parser import HTMLParser
from pathlib import Path

EXPECTED_PAGES = (
    "index.html",
    "download.html",
    "smart-reporting.html",
    "features.html",
    "control.html",
    "architecture.html",
    "roadmap.html",
    "404.html",
)

EXPECTED_MEDIA = (
    "assets/social-card.png",
    "assets/screenshots/arsas-first-launch.webp",
    "assets/screenshots/arsas-multi-ied.webp",
    "assets/screenshots/arsas-live-values.webp",
    "assets/screenshots/arsas-event-log.webp",
    "assets/screenshots/arsas-goose.webp",
    "assets/screenshots/arsas-diagnostics.webp",
)

DIRECT_INSTALLER = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe"
DIRECT_PORTABLE = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip"
DIRECT_CHECKSUMS = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SHA256SUMS.txt"
ALLOWED_GITHUB_URLS = {DIRECT_INSTALLER, DIRECT_PORTABLE, DIRECT_CHECKSUMS}


class AssetParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.refs: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        for key in ("href", "src", "content"):
            value = values.get(key)
            if value:
                self.refs.append(value)


def png_size(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG")
    return struct.unpack(">II", data[16:24])


def validate_updater_manifest(site: Path, errors: list[str]) -> None:
    path = site / "latest.json"
    if not path.exists():
        errors.append("missing deployable file: latest.json")
        return

    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"latest.json: {exc}")
        return

    if manifest.get("schemaVersion") != 1:
        errors.append("latest.json: schemaVersion must be 1")
    if manifest.get("product") != "ARSAS":
        errors.append("latest.json: product must be ARSAS")
    if manifest.get("channel") != "stable":
        errors.append("latest.json: channel must be stable")
    if not re.fullmatch(r"\d+\.\d+\.\d+", str(manifest.get("version", ""))):
        errors.append("latest.json: version must use major.minor.patch")

    installer = manifest.get("installer")
    if not isinstance(installer, dict):
        errors.append("latest.json: installer object is missing")
        return
    if installer.get("name") != "ARSAS-Windows-x64-Setup.exe":
        errors.append("latest.json: installer name is not the stable ARSAS EXE")
    if installer.get("url") != DIRECT_INSTALLER:
        errors.append("latest.json: installer URL is not the approved direct stable EXE")
    if not re.fullmatch(r"[0-9a-fA-F]{64}", str(installer.get("sha256", ""))):
        errors.append("latest.json: installer SHA-256 is invalid")
    size = installer.get("sizeBytes")
    if not isinstance(size, int) or not 1_000_000 <= size <= 500_000_000:
        errors.append("latest.json: installer size is outside the accepted range")


def main() -> int:
    site = Path(sys.argv[1] if len(sys.argv) > 1 else "_site").resolve()
    errors: list[str] = []

    for relative in EXPECTED_PAGES + EXPECTED_MEDIA:
        if not (site / relative).exists():
            errors.append(f"missing deployable file: {relative}")

    combined = ""
    all_refs: list[tuple[str, str]] = []
    for page_name in EXPECTED_PAGES:
        page = site / page_name
        if not page.exists():
            continue
        text = page.read_text(encoding="utf-8")
        combined += text
        parser = AssetParser()
        parser.feed(text)
        for ref in parser.refs:
            all_refs.append((page_name, ref))
            if ref.startswith("assets/") and not (site / ref.split("?", 1)[0].split("#", 1)[0]).exists():
                errors.append(f"{page_name}: missing local asset {ref}")

    forbidden_text = (
        "raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot",
        "https://masarray.github.io/arsas/assets/social-card.svg",
        '"codeRepository"',
        ">Repository</a>",
        ">Issues</a>",
        "Build from source",
        "Build instructions",
    )
    for value in forbidden_text:
        if value in combined:
            errors.append(f"deployable HTML still exposes forbidden public route or text: {value}")

    for page_name, ref in all_refs:
        if ref.startswith("https://github.com/") and ref not in ALLOWED_GITHUB_URLS:
            errors.append(f"{page_name}: public website exposes a GitHub page instead of a direct binary: {ref}")

    if "https://masarray.github.io/arsas/assets/social-card.png" not in combined:
        errors.append("PNG social preview is not referenced")
    if DIRECT_INSTALLER not in combined:
        errors.append("latest stable installer direct-download URL is missing")
    if 'href="download.html"' in combined:
        errors.append("public CTA still opens an intermediate download page instead of the EXE")

    download = site / "download.html"
    if download.exists():
        text = download.read_text(encoding="utf-8")
        for required in (DIRECT_INSTALLER, DIRECT_PORTABLE, DIRECT_CHECKSUMS, "Latest stable channel"):
            if required not in text:
                errors.append(f"download page is missing {required}")
        if "download.js" in text:
            errors.append("download page still depends on repository-aware JavaScript")

    social = site / "assets/social-card.png"
    if social.exists():
        try:
            if png_size(social) != (1200, 630):
                errors.append(f"social-card.png must be 1200x630, found {png_size(social)}")
        except ValueError as exc:
            errors.append(f"social-card.png: {exc}")

    validate_updater_manifest(site, errors)

    if errors:
        print("Public landing validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Public landing validation passed: landing-only navigation, direct downloads, and the verified updater manifest are enforced.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
