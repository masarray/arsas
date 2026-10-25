#!/usr/bin/env python3
"""Audit non-pixel metadata in the public v1.6.42 PNG/WebP captures.

This is an automated hidden-metadata boundary, NOT proof that on-screen IP
addresses, device identities or customer information have been sanitized.
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path

CAPTURES = (
    "ied-browser", "event-logs", "signal-testing", "fat-report-preview",
    "smv", "goose", "annunciator", "diagnostics",
)
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
PRIVATE_PNG_CHUNKS = {b"tEXt", b"zTXt", b"iTXt", b"eXIf"}
PRIVATE_WEBP_CHUNKS = {b"EXIF", b"XMP "}


def png_metadata(data: bytes) -> list[str]:
    if not data.startswith(PNG_SIGNATURE):
        raise ValueError("not PNG")
    cursor = len(PNG_SIGNATURE)
    found: list[str] = []
    terminated = False
    while cursor + 12 <= len(data):
        length = int.from_bytes(data[cursor:cursor + 4], "big")
        kind = data[cursor + 4:cursor + 8]
        cursor += 8
        if length > len(data) - cursor - 4:
            raise ValueError("truncated PNG chunk")
        cursor += length + 4  # data + CRC
        if kind in PRIVATE_PNG_CHUNKS:
            found.append(kind.decode("ascii"))
        if kind == b"IEND":
            terminated = True
            break
    if not terminated or cursor != len(data):
        raise ValueError("missing/invalid PNG end")
    return found


def webp_metadata(data: bytes) -> list[str]:
    if len(data) < 12 or data[:4] != b"RIFF" or data[8:12] != b"WEBP":
        raise ValueError("not WebP")
    length = struct.unpack_from("<I", data, 4)[0] + 8
    if length != len(data):
        raise ValueError("invalid RIFF length")
    cursor = 12
    found: list[str] = []
    while cursor + 8 <= len(data):
        kind = data[cursor:cursor + 4]
        size = struct.unpack_from("<I", data, cursor + 4)[0]
        cursor += 8
        if size > len(data) - cursor:
            raise ValueError("truncated WebP chunk")
        if kind in PRIVATE_WEBP_CHUNKS:
            found.append(kind.decode("ascii").strip())
        cursor += size + (size & 1)
    if cursor != len(data):
        raise ValueError("invalid WebP chunk alignment")
    return found


def audit(root: Path) -> list[dict[str, object]]:
    screens = root / "landing" / "assets" / "screenshots"
    results: list[dict[str, object]] = []
    for name in CAPTURES:
        for ext, scanner in (("png", png_metadata), ("webp", webp_metadata)):
            path = screens / f"arsas-{name}-v1.6.42.{ext}"
            try:
                data = path.read_bytes()
                metadata = scanner(data)
                status = "PASS" if not metadata else "FAIL"
                finding = ",".join(metadata)
            except (OSError, ValueError) as exc:
                status = "FAIL"
                finding = type(exc).__name__  # never copy potentially private metadata
            results.append({
                "file": path.name, "status": status, "metadata_chunks": finding
            })
    # Current Add IED dialog is a distinct approved crop from a later original.
    crop = screens / "arsas-add-ied-connect-dialog-v1.6.42.webp"
    try:
        metadata = webp_metadata(crop.read_bytes())
        results.append({"file": crop.name, "status": "PASS" if not metadata else "FAIL",
                        "metadata_chunks": ",".join(metadata)})
    except (OSError, ValueError) as exc:
        results.append({"file": crop.name, "status": "FAIL",
                        "metadata_chunks": type(exc).__name__})
    return results


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    rows = audit(args.root)
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps({"scope": "metadata-only", "screens": rows},
                                          indent=2) + "\n", encoding="utf-8")
    failures = [row for row in rows if row["status"] != "PASS"]
    print(f"Public media embedded-metadata audit: {len(rows)-len(failures)}/{len(rows)} clean source assets.")
    for row in failures:
        print(f"- {row['file']}: {row['metadata_chunks']}", file=sys.stderr)
    if failures:
        print("Inspect the original image and seek owner approval before replacing public evidence.", file=sys.stderr)
    print("Pixel-level private-network/IED identity review remains a separate human gate.")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
