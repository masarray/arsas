#!/usr/bin/env python3
"""Verify that public Pages exposes P3 adoption and field-proof surfaces."""

from __future__ import annotations

import argparse
import json
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urljoin
from urllib.request import Request, urlopen


def fetch(url: str) -> tuple[int, str]:
    request = Request(url, headers={"User-Agent": "ARSAS-Adoption-Attestation/1.0", "Cache-Control": "no-cache"})
    try:
        with urlopen(request, timeout=25) as response:
            return response.status, response.read().decode("utf-8", errors="replace")
    except HTTPError as exc:
        return exc.code, exc.read().decode("utf-8", errors="replace")
    except URLError as exc:
        raise RuntimeError(str(exc.reason)) from exc


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--source-commit", required=True)
    args = parser.parse_args()
    base = args.base_url.rstrip("/") + "/"
    nonce = urlencode({"adoption": args.source_commit})
    checks = {
        "quick-start.html": ("quick-step-number", "data-responsive-media=\"webp\"", "panduan-mulai-arsas.html"),
        "faq.html": ('"@type":"FAQPage"', "faq-item", "faq-arsas.html"),
        "compatibility.html": ("field-profile-a-file-service", "field-profile-b-rcb-export", "device-evidence.json"),
        "demo.html": ("data-guided-demo", "data-demo-step", "demo.js", "data-responsive-media=\"webp\""),
        "guides.html": ("data-guide-filter", "data-guide-card", "guide-filter.js"),
    }
    errors: list[str] = []
    for path, required in checks.items():
        status, body = fetch(urljoin(base, path) + "?" + nonce)
        if status != 200:
            errors.append(f"{path} returned HTTP {status}")
            continue
        for marker in required:
            if marker not in body:
                errors.append(f"{path} is missing {marker}")
    status, body = fetch(urljoin(base, "device-evidence.json") + "?" + nonce)
    if status != 200:
        errors.append(f"device-evidence.json returned HTTP {status}")
    else:
        try:
            evidence = json.loads(body)
        except json.JSONDecodeError as exc:
            errors.append(f"device-evidence.json is invalid JSON: {exc}")
        else:
            if evidence.get("namedDeviceCount") != 0 or len(evidence.get("profiles", [])) != 2:
                errors.append("public compatibility evidence boundary is invalid")
    if errors:
        print("Public ARSAS adoption attestation failed:")
        for error in errors:
            print(f"- {error}")
        return 1
    print("Public ARSAS adoption attestation passed: Quick Start, FAQ, compatibility, demo, filters and responsive media are live.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
