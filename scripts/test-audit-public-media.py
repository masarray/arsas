#!/usr/bin/env python3
"""Deterministic positive/negative fixtures for the public screenshot metadata gate."""

from __future__ import annotations

import importlib.util
import struct
import unittest
import zlib
from pathlib import Path

module_path = Path(__file__).with_name("audit-public-media.py")
spec = importlib.util.spec_from_file_location("audit_public_media", module_path)
if spec is None or spec.loader is None:
    raise RuntimeError("Unable to import public media audit")
media = importlib.util.module_from_spec(spec)
spec.loader.exec_module(media)


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    return (struct.pack(">I", len(payload)) + kind + payload
            + struct.pack(">I", zlib.crc32(kind + payload)))


def webp_chunk(kind: bytes, payload: bytes) -> bytes:
    return kind + struct.pack("<I", len(payload)) + payload + (b"\x00" if len(payload) & 1 else b"")


class PublicMediaTests(unittest.TestCase):
    def test_clean_png_passes(self) -> None:
        png = media.PNG_SIGNATURE + png_chunk(b"IHDR", b"\x00" * 13) + png_chunk(b"IEND", b"")
        self.assertEqual(media.png_metadata(png), [])

    def test_png_text_metadata_fails(self) -> None:
        png = (media.PNG_SIGNATURE + png_chunk(b"IHDR", b"\x00" * 13)
               + png_chunk(b"iTXt", b"private") + png_chunk(b"IEND", b""))
        self.assertEqual(media.png_metadata(png), ["iTXt"])

    def test_truncated_png_fails(self) -> None:
        with self.assertRaises(ValueError):
            media.png_metadata(media.PNG_SIGNATURE + b"\x00" * 10)

    def test_clean_webp_passes(self) -> None:
        payload = webp_chunk(b"VP8L", b"\x00\x00")
        webp = b"RIFF" + struct.pack("<I", len(payload) + 4) + b"WEBP" + payload
        self.assertEqual(media.webp_metadata(webp), [])

    def test_webp_exif_fails(self) -> None:
        payload = webp_chunk(b"VP8L", b"\x00\x00") + webp_chunk(b"EXIF", b"private")
        webp = b"RIFF" + struct.pack("<I", len(payload) + 4) + b"WEBP" + payload
        self.assertEqual(media.webp_metadata(webp), ["EXIF"])

    def test_bad_webp_size_fails(self) -> None:
        with self.assertRaises(ValueError):
            media.webp_metadata(b"RIFF" + struct.pack("<I", 90) + b"WEBP")


if __name__ == "__main__":
    unittest.main()
