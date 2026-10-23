#!/usr/bin/env python3
"""Submit the rendered ARSAS sitemap URLs to an IndexNow endpoint."""

from __future__ import annotations

import argparse
import json
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path
from urllib.parse import urlparse

DEFAULT_ENDPOINT = "https://api.indexnow.org/indexnow"
DEFAULT_CANONICAL_ROOT = "https://masarray.github.io/arsas/"


def read_urls(sitemap: Path) -> list[str]:
    try:
        root = ET.parse(sitemap).getroot()
    except (OSError, ET.ParseError) as exc:
        raise SystemExit(f"Cannot read sitemap {sitemap}: {exc}") from exc
    namespace = {"sm": "http://www.sitemaps.org/schemas/sitemap/0.9"}
    urls = [node.text.strip() for node in root.findall("sm:url/sm:loc", namespace) if node.text and node.text.strip()]
    if not urls:
        raise SystemExit("Sitemap does not contain indexable URLs")
    if len(urls) > 10_000:
        raise SystemExit("IndexNow request exceeds the 10,000 URL protocol limit")
    return list(dict.fromkeys(urls))


def read_key(path: Path) -> str:
    try:
        key = path.read_text(encoding="utf-8").strip()
    except OSError as exc:
        raise SystemExit(f"Cannot read IndexNow key file {path}: {exc}") from exc
    if not 8 <= len(key) <= 128 or not all(char.isalnum() or char == "-" for char in key):
        raise SystemExit("IndexNow key must be 8-128 alphanumeric or dash characters")
    if path.stem != key:
        raise SystemExit("IndexNow key filename must match its content")
    return key


def validate_scope(urls: list[str], canonical_root: str) -> tuple[str, str]:
    root = canonical_root.rstrip("/") + "/"
    parsed = urlparse(root)
    if parsed.scheme != "https" or not parsed.netloc:
        raise SystemExit("Canonical root must be an absolute HTTPS URL")
    for url in urls:
        if not url.startswith(root):
            raise SystemExit(f"Sitemap URL is outside the ARSAS site scope: {url}")
    return parsed.netloc, root


def submit(endpoint: str, host: str, key: str, key_location: str, urls: list[str], timeout: int) -> int:
    payload = json.dumps({"host": host, "key": key, "keyLocation": key_location, "urlList": urls}).encode("utf-8")
    request = urllib.request.Request(endpoint, data=payload, method="POST", headers={"Content-Type": "application/json; charset=utf-8", "User-Agent": "ARSAS-IndexNow/1.0"})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            status = response.getcode()
    except urllib.error.HTTPError as exc:
        status = exc.code
        body = exc.read().decode("utf-8", errors="replace").strip()
        print(f"IndexNow returned HTTP {status}: {body or exc.reason}", file=sys.stderr)
    except urllib.error.URLError as exc:
        print(f"IndexNow request failed: {exc.reason}", file=sys.stderr)
        return 1
    if status in (200, 202):
        print(f"IndexNow accepted {len(urls)} ARSAS URLs with HTTP {status}.")
        return 0
    print(f"IndexNow did not accept the submission (HTTP {status}).", file=sys.stderr)
    return 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sitemap", default="_site/sitemap.xml")
    parser.add_argument("--key-file", default="landing/arsas-iec61850-20260720-6f4a9d2c8b.txt")
    parser.add_argument("--canonical-root", default=DEFAULT_CANONICAL_ROOT)
    parser.add_argument("--endpoint", default=DEFAULT_ENDPOINT)
    parser.add_argument("--timeout", type=int, default=20)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    sitemap = Path(args.sitemap).resolve()
    key_path = Path(args.key_file).resolve()
    urls = read_urls(sitemap)
    key = read_key(key_path)
    host, canonical_root = validate_scope(urls, args.canonical_root)
    key_location = canonical_root + key_path.name

    if args.dry_run:
        print(json.dumps({"host": host, "key": key, "keyLocation": key_location, "urlCount": len(urls)}, indent=2))
        return 0
    return submit(args.endpoint, host, key, key_location, urls, args.timeout)


if __name__ == "__main__":
    raise SystemExit(main())
