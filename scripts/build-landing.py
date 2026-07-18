#!/usr/bin/env python3
"""Build the public ARSAS website with local media and direct binary downloads."""

from __future__ import annotations

import argparse
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

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
QUICK_START = "https://github.com/masarray/arsas#quick-start"
DIRECT_INSTALLER = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe"
DIRECT_PORTABLE = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip"
DIRECT_CHECKSUMS = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SHA256SUMS.txt"

PROTECTED_DOWNLOADS = {
    DIRECT_INSTALLER: "__ARSAS_PUBLIC_INSTALLER__",
    DIRECT_PORTABLE: "__ARSAS_PUBLIC_PORTABLE__",
    DIRECT_CHECKSUMS: "__ARSAS_PUBLIC_CHECKSUMS__",
}


def rewrite_public_html(text: str) -> str:
    for url, placeholder in PROTECTED_DOWNLOADS.items():
        text = text.replace(url, placeholder)

    for remote, local in SCREENSHOT_MAP.items():
        text = text.replace(remote, local)

    text = text.replace(SOCIAL_SVG, SOCIAL_PNG)
    text = text.replace(
        '<link rel="preconnect" href="https://raw.githubusercontent.com" crossorigin />',
        "",
    )

    # Product CTAs must start the stable installer download, not open the source repository.
    text = text.replace(f'href="{QUICK_START}"', 'href="__ARSAS_PUBLIC_INSTALLER__" download')
    text = text.replace('href="download.html"', 'href="__ARSAS_PUBLIC_INSTALLER__" download')
    text = text.replace(
        '"downloadUrl": "https://masarray.github.io/arsas/download.html"',
        '"downloadUrl": "__ARSAS_PUBLIC_INSTALLER__"',
    )
    text = text.replace(
        f'"downloadUrl": "{QUICK_START}"',
        '"downloadUrl": "__ARSAS_PUBLIC_INSTALLER__"',
    )

    # Remove source-repository discovery from public structured data.
    text = re.sub(
        r'\s*"codeRepository"\s*:\s*"https://github\.com/masarray/arsas"\s*,?',
        "",
        text,
    )

    # Convert every remaining project/repository route into a landing-page route.
    replacements = (
        ("https://github.com/masarray/arsas/blob/main/docs/ARCHITECTURE.md#report-first-acquisition", "architecture.html"),
        ("https://github.com/masarray/arsas/blob/main/ROADMAP.md", "roadmap.html"),
        ("https://github.com/masarray/arsas/issues", "roadmap.html"),
        ("https://github.com/masarray/arsas/releases", "__ARSAS_PUBLIC_INSTALLER__"),
        ("https://github.com/masarray/ARIEC61850", "architecture.html"),
        ("https://github.com/masarray/arsas", "./"),
    )
    for old, new in replacements:
        text = text.replace(old, new)

    # Keep labels meaningful after repository routes are removed.
    text = text.replace('>Repository</a>', '>Architecture</a>')
    text = text.replace('>Issues</a>', '>Roadmap</a>')
    text = text.replace('>Engine</a>', '>Architecture</a>')
    text = text.replace('>Build instructions</a>', '>Download installer</a>')
    text = text.replace('>All releases</a>', '>Download installer</a>')

    for url, placeholder in PROTECTED_DOWNLOADS.items():
        text = text.replace(placeholder, url)

    return text


def build(source: Path, output: Path) -> None:
    if output.exists():
        shutil.rmtree(output)
    shutil.copytree(source, output)

    replacements = 0
    for page in output.glob("*.html"):
        text = page.read_text(encoding="utf-8")
        rewritten = rewrite_public_html(text)
        if rewritten != text:
            replacements += 1
            page.write_text(rewritten, encoding="utf-8")

    required = [output / path for path in (
        "assets/social-card.png",
        "assets/screenshots/arsas-first-launch.webp",
        "assets/screenshots/arsas-multi-ied.webp",
        "assets/screenshots/arsas-live-values.webp",
        "assets/screenshots/arsas-event-log.webp",
        "assets/screenshots/arsas-goose.webp",
        "assets/screenshots/arsas-diagnostics.webp",
        "download.html",
        "download.css",
    )]
    missing = [str(path.relative_to(output)) for path in required if not path.exists()]
    if missing:
        raise SystemExit("Missing deployable landing assets: " + ", ".join(missing))

    deployed_text = "\n".join(path.read_text(encoding="utf-8") for path in output.glob("*.html"))
    if "raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot" in deployed_text:
        raise SystemExit("Remote screenshot URL remains in deployable landing artifact")
    if SOCIAL_SVG in deployed_text:
        raise SystemExit("SVG social card remains referenced in deployable landing artifact")
    if replacements == 0:
        raise SystemExit("Landing build made no expected transformations")

    print(f"Built public ARSAS website at {output} ({replacements} transformed pages).")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default=str(ROOT / "landing"))
    parser.add_argument("--output", default=str(ROOT / "_site"))
    args = parser.parse_args()
    build(Path(args.source).resolve(), Path(args.output).resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
