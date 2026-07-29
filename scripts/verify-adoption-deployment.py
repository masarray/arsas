#!/usr/bin/env python3
"""Verify that public Pages exposes learning, capability tutorials, evidence and IO FAT surfaces."""

from __future__ import annotations

import argparse
import json
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urljoin
from urllib.request import Request, urlopen


def fetch(url: str) -> tuple[int, str]:
    request = Request(url, headers={"User-Agent": "ARSAS-Adoption-Attestation/1.2", "Cache-Control": "no-cache"})
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
        "learning-center.html": ("Learn IEC 61850", "What is IEC 61850", "Connect your first IED", "pusat-belajar-iec61850.html"),
        "pusat-belajar-iec61850.html": ("Belajar IEC 61850", "Apa itu IEC 61850", "Hubungkan IED pertama", "learning-center.html"),
        "what-is-iec61850.html": ("30-second takeaway", "Logical Node", "Reporting", "GOOSE", "FAQPage", "apa-itu-iec61850.html"),
        "apa-itu-iec61850.html": ("Takeaway 30 detik", "Logical Node", "Reporting", "GOOSE", "FAQPage", "what-is-iec61850.html"),
        "connect-ied-ip-arsas.html": ("TCP port 102", "Add IED", "MMS association", "HowTo", "cara-hubungkan-ied-ip-arsas.html"),
        "cara-hubungkan-ied-ip-arsas.html": ("TCP port 102", "Add IED", "association MMS", "HowTo", "connect-ied-ip-arsas.html"),
        "mms-client.html": ("30-second takeaway", "Add IED", "Functional Constraint", "Success criteria", "If it fails", "HowTo", "FAQPage", "mms-client-iec61850.html"),
        "mms-client-iec61850.html": ("Takeaway 30 detik", "Add IED", "Functional Constraint", "Success criteria", "Bila gagal", "HowTo", "FAQPage", "mms-client.html"),
        "smart-reporting.html": ("30-second takeaway", "DataSet", "BRCB", "URCB", "Success criteria", "If Reporting is silent", "HowTo", "FAQPage", "smart-reporting-iec61850.html"),
        "smart-reporting-iec61850.html": ("Takeaway 30 detik", "DataSet", "BRCB", "URCB", "Success criteria", "Bila Reporting silent", "HowTo", "FAQPage", "smart-reporting.html"),
        "quick-start.html": ("quick-step-number", "data-responsive-media=\"webp\"", "panduan-mulai-arsas.html", "io-list-fat-evidence.html"),
        "io-list-fat-evidence.html": ("OFF → ON", "ON → OFF", "TestPointId", ".arsas", "Native PDF report", "bukti-fat-iolist-iec61850.html"),
        "bukti-fat-iolist-iec61850.html": ("OFF → ON", "ON → OFF", "TestPointId", ".arsas", "Report PDF native", "io-list-fat-evidence.html"),
        "faq.html": ('"@type":"FAQPage"', "faq-item", "faq-arsas.html"),
        "compatibility.html": ("field-profile-a-file-service", "field-profile-b-rcb-export", "device-evidence.json"),
        "demo.html": ("data-guided-demo", "data-demo-step", "demo.js", "data-responsive-media=\"webp\""),
        "guides.html": ("data-guide-filter", "data-guide-card", "guide-filter.js", "io-list-fat-evidence.html"),
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
    print("Public ARSAS adoption attestation passed: Learning Center, beginner IEC 61850 guides, bilingual MMS and Reporting tutorials, Quick Start, IO FAT evidence, FAQ, compatibility, demo, filters and responsive media are live.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
