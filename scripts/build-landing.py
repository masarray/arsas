#!/usr/bin/env python3
"""Build the deployable ARSAS website with self-hosted media and stable download CTAs."""

from __future__ import annotations

import argparse
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
DOWNLOAD_PAGE = "https://masarray.github.io/arsas/download.html"


def build(source: Path, output: Path) -> None:
    if output.exists():
        shutil.rmtree(output)
    shutil.copytree(source, output)

    replacements = 0
    for page in output.glob("*.html"):
        text = page.read_text(encoding="utf-8")
        original = text

        for remote, local in SCREENSHOT_MAP.items():
            text = text.replace(remote, local)

        text = text.replace(SOCIAL_SVG, SOCIAL_PNG)
        text = text.replace(
            '<link rel="preconnect" href="https://raw.githubusercontent.com" crossorigin />',
            "",
        )
        if page.name != "download.html":
            text = text.replace(
                f'href="{QUICK_START}"',
                'href="download.html"',
            )
        text = text.replace(
            f'"downloadUrl": "{QUICK_START}"',
            f'"downloadUrl": "{DOWNLOAD_PAGE}"',
        )

        if text != original:
            replacements += 1
            page.write_text(text, encoding="utf-8")

    required = [output / path for path in (
        "assets/social-card.png",
        "assets/screenshots/arsas-first-launch.webp",
        "assets/screenshots/arsas-multi-ied.webp",
        "assets/screenshots/arsas-live-values.webp",
        "assets/screenshots/arsas-event-log.webp",
        "assets/screenshots/arsas-goose.webp",
        "assets/screenshots/arsas-diagnostics.webp",
        "download.html",
        "download.js",
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

    print(f"Built deployable ARSAS website at {output} ({replacements} transformed pages).")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default=str(ROOT / "landing"))
    parser.add_argument("--output", default=str(ROOT / "_site"))
    args = parser.parse_args()
    build(Path(args.source).resolve(), Path(args.output).resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
