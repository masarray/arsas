#!/usr/bin/env python3
"""Build the ARSAS Open Graph card from branded, reviewable source assets."""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_BACKGROUND = ROOT / "Assets" / "social" / "arsas-og-industrial-background.webp"
DEFAULT_SCREENSHOT = ROOT / "Assets" / "screenshot" / "arsas-overview-v1.6.19.webp"
DEFAULT_ICON = ROOT / "Assets" / "app-icon.png"
DEFAULT_OUTPUT = ROOT / "landing" / "assets" / "social-card.png"
CANVAS_SIZE = (1200, 630)


def cover(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    scale = max(size[0] / image.width, size[1] / image.height)
    resized = image.resize(
        (round(image.width * scale), round(image.height * scale)),
        Image.Resampling.LANCZOS,
    )
    left = (resized.width - size[0]) // 2
    top = (resized.height - size[1]) // 2
    return resized.crop((left, top, left + size[0], top + size[1]))


def contain(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    copy = image.copy()
    copy.thumbnail(size, Image.Resampling.LANCZOS)
    return copy


def font(path: Path, size: int) -> ImageFont.FreeTypeFont:
    if not path.is_file():
        raise SystemExit(f"Required font is missing: {path}")
    return ImageFont.truetype(str(path), size=size)


def rounded_image(image: Image.Image, radius: int) -> Image.Image:
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, image.width, image.height), radius=radius, fill=255)
    result = image.convert("RGBA")
    result.putalpha(mask)
    return result


def build(background_path: Path, screenshot_path: Path, icon_path: Path, output_path: Path) -> None:
    for source in (background_path, screenshot_path, icon_path):
        if not source.is_file():
            raise SystemExit(f"Social-card source is missing: {source}")

    canvas = cover(Image.open(background_path).convert("RGB"), CANVAS_SIZE).convert("RGBA")

    # Lock the left side to a quiet, high-contrast reading surface while preserving
    # the generated substation atmosphere on the right.
    overlay = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 0))
    overlay_draw = ImageDraw.Draw(overlay)
    for x in range(CANVAS_SIZE[0]):
        t = x / CANVAS_SIZE[0]
        alpha = round(176 * max(0.0, 1.0 - t / 0.72))
        overlay_draw.line((x, 0, x, CANVAS_SIZE[1]), fill=(2, 9, 20, alpha))
    overlay_draw.rectangle((0, 0, 1200, 630), outline=(125, 211, 252, 42), width=2)
    canvas = Image.alpha_composite(canvas, overlay)

    regular = font(Path(r"C:\Windows\Fonts\segoeui.ttf"), 23)
    small = font(Path(r"C:\Windows\Fonts\segoeui.ttf"), 18)
    label = font(Path(r"C:\Windows\Fonts\segoeuib.ttf"), 28)
    heading = font(Path(r"C:\Windows\Fonts\segoeuib.ttf"), 42)
    feature = font(Path(r"C:\Windows\Fonts\segoeuib.ttf"), 17)

    draw = ImageDraw.Draw(canvas)
    icon = contain(Image.open(icon_path).convert("RGBA"), (66, 66))
    canvas.alpha_composite(icon, (62, 44))
    draw.text((146, 58), "ARSAS", font=label, fill=(244, 249, 255, 255))
    draw.text((146, 91), "IEC 61850 WORKSTATION", font=small, fill=(139, 214, 252, 255))

    draw.rounded_rectangle((62, 142, 140, 149), radius=4, fill=(64, 205, 232, 255))
    draw.multiline_text(
        (62, 174),
        "IEC 61850 engineering.\nFrom live model to evidence.",
        font=heading,
        fill=(248, 251, 255, 255),
        spacing=4,
    )
    draw.multiline_text(
        (64, 316),
        "Open-source Windows software for learning,\nFAT, troubleshooting and traceable evidence.",
        font=regular,
        fill=(190, 207, 227, 255),
        spacing=8,
    )

    # Exact product screenshot. This stays deterministic so UI text and evidence
    # are never hallucinated by the background-generation step.
    screenshot = contain(Image.open(screenshot_path).convert("RGB"), (480, 300))
    screenshot = rounded_image(screenshot, 18)
    sx, sy = 690, 195
    shadow = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    shadow_draw.rounded_rectangle(
        (sx - 16, sy - 14, sx + screenshot.width + 18, sy + screenshot.height + 24),
        radius=28,
        fill=(0, 5, 14, 160),
    )
    shadow = shadow.filter(ImageFilter.GaussianBlur(20))
    canvas = Image.alpha_composite(canvas, shadow)
    canvas.alpha_composite(screenshot, (sx, sy))
    draw = ImageDraw.Draw(canvas)
    draw.rounded_rectangle(
        (sx - 1, sy - 1, sx + screenshot.width, sy + screenshot.height),
        radius=19,
        outline=(189, 229, 255, 150),
        width=2,
    )

    feature_text = "LIVE DISCOVERY   /   IO LIST FAT   /   REPORTING   /   GOOSE   /   SCL"
    draw.text((64, 476), feature_text, font=feature, fill=(139, 220, 250, 255))
    draw.line((64, 526, 1136, 526), fill=(147, 197, 226, 72), width=1)
    draw.text((64, 554), "masarray.github.io/arsas", font=small, fill=(224, 235, 247, 255))
    draw.text((908, 554), "FREE  /  WINDOWS", font=feature, fill=(224, 235, 247, 255))

    output_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.convert("RGB").save(output_path, format="PNG", optimize=True)
    if Image.open(output_path).size != CANVAS_SIZE:
        raise SystemExit(f"Unexpected social-card dimensions: {Image.open(output_path).size}")
    print(f"Built ARSAS social card at {output_path} ({CANVAS_SIZE[0]}x{CANVAS_SIZE[1]}).")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--background", type=Path, default=DEFAULT_BACKGROUND)
    parser.add_argument("--screenshot", type=Path, default=DEFAULT_SCREENSHOT)
    parser.add_argument("--icon", type=Path, default=DEFAULT_ICON)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    build(args.background.resolve(), args.screenshot.resolve(), args.icon.resolve(), args.output.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
