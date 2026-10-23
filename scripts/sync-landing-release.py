#!/usr/bin/env python3
"""Synchronize public landing release notes from verified stable release evidence.

The stable package identity (version, release URL, signing state and published assets)
is authoritative. Manually curated release notes are preserved when they already match
the published version; otherwise a conservative bilingual release summary is generated
from the GitHub Release body plus verified package evidence.
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

SEMVER = re.compile(r"\d+\.\d+\.\d+")
SHA256 = re.compile(r"[0-9a-fA-F]{64}")
DEFAULT_ISSUES_URL = "https://github.com/masarray/arsas/issues/new/choose"
DEFAULT_SCREENSHOT = {
    "src": "assets/screenshots/arsas-live-values.webp",
    "width": 1507,
    "height": 893,
}


def read_object(path: Path, label: str) -> dict[str, object]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise SystemExit(f"Cannot read {label}: {exc}") from exc
    if not isinstance(value, dict):
        raise SystemExit(f"{label} must contain a JSON object")
    return value


def valid_text(value: object) -> bool:
    return isinstance(value, str) and bool(value.strip())


def valid_list(value: object, minimum: int = 4) -> bool:
    return isinstance(value, list) and len(value) >= minimum and all(valid_text(item) for item in value)


def release_changes(release: dict[str, object]) -> list[str]:
    body = release.get("body")
    if not isinstance(body, str):
        return []
    changes: list[str] = []
    for raw in body.splitlines():
        line = raw.strip()
        match = re.match(r"^[*-]\s+(.+?)(?:\s+by\s+@[^\s]+)?(?:\s+in\s+https?://\S+)?$", line)
        if not match:
            continue
        title = match.group(1).strip()
        title = re.sub(r"\s+by\s+@[^\s]+.*$", "", title).strip()
        if not title or title.lower().startswith("release:") or title in changes:
            continue
        changes.append(title)
    return changes[:5]


def id_change_title(title: str) -> str:
    replacements = (
        ("feat:", "Fitur:"),
        ("fix:", "Perbaikan:"),
        ("docs:", "Dokumentasi:"),
        ("chore:", "Pemeliharaan:"),
        ("perf:", "Performa:"),
        ("refactor:", "Refactor:"),
    )
    lowered = title.lower()
    for prefix, replacement in replacements:
        if lowered.startswith(prefix):
            return replacement + title[len(prefix):]
    return "Perubahan rilis: " + title


def signing_notes(status: str, evidence_detail: str) -> dict[str, str]:
    if status == "signed":
        return {
            "status": "signed",
            "label": "Authenticode-signed",
            "labelId": "Ditandatangani dengan Authenticode",
            "detail": evidence_detail,
            "detailId": "Binary Windows publik membawa signature Authenticode. Tetap verifikasi SHA-256 yang dipublikasikan sebelum digunakan.",
        }
    return {
        "status": "unsigned",
        "label": "Not Authenticode-signed",
        "labelId": "Belum ditandatangani dengan Authenticode",
        "detail": evidence_detail,
        "detailId": "Installer Windows dan portable EXE publik saat ini belum memiliki commercial Authenticode publisher signature. Verifikasi nilai SHA-256 yang dipublikasikan sebelum digunakan. Karena itu peringatan SmartScreen masih mungkin muncul dan status ini tidak disembunyikan dari user.",
    }


def generated_notes(
    evidence: dict[str, object],
    release: dict[str, object],
    previous: dict[str, object],
) -> dict[str, object]:
    version = str(evidence["version"])
    changes = release_changes(release)

    highlights = list(changes)
    fallback_highlights = [
        f"The verified Windows installer is published as the stable ARSAS {version} package.",
        "The portable single EXE is published from the same verified stable release.",
        "SHA-256 checksums, SPDX SBOM and release provenance are published alongside the Windows binaries.",
        "The public Download Center resolves package links through the latest stable GitHub Release authority.",
    ]
    for item in fallback_highlights:
        if len(highlights) >= 4:
            break
        highlights.append(item)

    highlights_id = [id_change_title(item) for item in changes]
    fallback_highlights_id = [
        f"Installer Windows terverifikasi dipublikasikan sebagai paket stabil ARSAS {version}.",
        "Portable single EXE dipublikasikan dari stable release terverifikasi yang sama.",
        "Checksum SHA-256, SPDX SBOM, dan provenance release dipublikasikan bersama binary Windows.",
        "Download Center publik menggunakan authority GitHub Release stabil terbaru untuk link paket.",
    ]
    for item in fallback_highlights_id:
        if len(highlights_id) >= 4:
            break
        highlights_id.append(item)

    improvements = [
        "Stable release identity, publish date, package size and SHA-256 are sourced from verified release evidence rather than hand-maintained version text.",
        "The Windows installer and portable download URLs use GitHub's releases/latest/download endpoints so the public buttons always resolve to the newest stable assets.",
        "Release notes are synchronized before website deployment, preventing a newer binary release from being blocked by stale landing-page metadata.",
        "The website deployment is explicitly dispatched after release synchronization so GitHub Actions token commits cannot leave Pages behind the published release.",
    ]
    improvements_id = [
        "Identitas stable release, tanggal publikasi, ukuran paket, dan SHA-256 diambil dari evidence release terverifikasi, bukan teks versi yang dipelihara manual.",
        "URL installer Windows dan portable memakai endpoint releases/latest/download GitHub sehingga tombol publik selalu menuju asset stabil terbaru.",
        "Catatan rilis disinkronkan sebelum deployment website agar binary release baru tidak terblokir metadata landing page yang tertinggal.",
        "Deployment website dipicu eksplisit setelah sinkronisasi release sehingga commit dari GitHub Actions token tidak membuat Pages tertinggal dari release publik.",
    ]

    old_limits = previous.get("knownLimitations")
    old_limits_id = previous.get("knownLimitationsId")
    if not valid_list(old_limits):
        old_limits = [
            "Physical relay validation remains necessary for vendor-specific values, report behavior and field network conditions that cannot be reproduced by CI.",
            "Windows x64 is the only packaged desktop platform in this stable release.",
            "The public binaries may trigger Windows SmartScreen when they are not Authenticode-signed.",
            "Raw-Ethernet GOOSE and Sampled Values workflows require an approved Npcap installation and suitable capture permissions.",
        ]
    if not valid_list(old_limits_id):
        old_limits_id = [
            "Validasi relay fisik tetap diperlukan untuk value vendor-specific, perilaku report, dan kondisi network lapangan yang tidak dapat direproduksi oleh CI.",
            "Windows x64 adalah satu-satunya platform desktop yang dipaketkan pada stable release ini.",
            "Binary publik dapat memicu Windows SmartScreen ketika belum ditandatangani dengan Authenticode.",
            "Workflow raw-Ethernet GOOSE dan Sampled Values memerlukan instalasi Npcap yang disetujui dan capture permission yang sesuai.",
        ]

    evidence_signing = evidence["codeSigning"]
    assert isinstance(evidence_signing, dict)
    status = str(evidence_signing["status"])
    signing = signing_notes(status, str(evidence_signing.get("detail", "")))

    previous_screenshot = previous.get("screenshot")
    screenshot = dict(DEFAULT_SCREENSHOT)
    if isinstance(previous_screenshot, dict):
        for key in ("src", "width", "height"):
            if key in previous_screenshot:
                screenshot[key] = previous_screenshot[key]
    screenshot.update(
        {
            "alt": f"ARSAS {version} Engineering and FAT live IEC 61850 workspace",
            "altId": f"Workspace live IEC 61850 Engineering dan FAT ARSAS {version}",
            "caption": f"ARSAS {version} stable Windows release with verified installer, portable package and release evidence.",
            "captionId": f"Stable release Windows ARSAS {version} dengan installer, portable package, dan release evidence yang terverifikasi.",
        }
    )

    issues_url = previous.get("issuesUrl") if valid_text(previous.get("issuesUrl")) else DEFAULT_ISSUES_URL
    return {
        "schemaVersion": 1,
        "product": "ARSAS",
        "version": version,
        "title": f"ARSAS {version} stable release",
        "titleId": f"Stable release ARSAS {version}",
        "summary": f"ARSAS {version} is the latest verified stable Windows release. Package identity, download links, checksums and publication evidence are synchronized automatically from the tagged GitHub Release.",
        "summaryId": f"ARSAS {version} adalah stable release Windows terverifikasi terbaru. Identitas paket, link download, checksum, dan publication evidence disinkronkan otomatis dari GitHub Release bertag.",
        "highlights": highlights,
        "highlightsId": highlights_id,
        "improvements": improvements,
        "improvementsId": improvements_id,
        "knownLimitations": old_limits,
        "knownLimitationsId": old_limits_id,
        "codeSigning": signing,
        "screenshot": screenshot,
        "issuesUrl": issues_url,
        "releaseUrl": evidence["releaseUrl"],
    }


def synchronize(evidence: dict[str, object], release: dict[str, object], previous: dict[str, object]) -> dict[str, object]:
    if evidence.get("schemaVersion") != 1 or evidence.get("product") != "ARSAS" or evidence.get("channel") != "stable":
        raise SystemExit("Stable release evidence has invalid identity")
    version = str(evidence.get("version", ""))
    if not SEMVER.fullmatch(version):
        raise SystemExit("Stable release version must use major.minor.patch")
    if release.get("draft") or release.get("prerelease"):
        raise SystemExit("Landing pages may only synchronize a published stable release")
    if release.get("tag_name") != f"v{version}":
        raise SystemExit("GitHub Release tag does not match stable evidence version")
    if release.get("html_url") != evidence.get("releaseUrl"):
        raise SystemExit("GitHub Release URL does not match stable evidence")

    for name in ("installer", "portable"):
        package = evidence.get(name)
        if not isinstance(package, dict) or not SHA256.fullmatch(str(package.get("sha256", ""))):
            raise SystemExit(f"Stable release {name} evidence is invalid")
        url = str(package.get("url", ""))
        if "/releases/latest/download/" not in url:
            raise SystemExit(f"Stable release {name} URL must use releases/latest/download")

    signing = evidence.get("codeSigning")
    if not isinstance(signing, dict) or signing.get("status") not in {"signed", "unsigned"}:
        raise SystemExit("Stable release signing evidence is invalid")

    # Keep curated copy when a human already prepared notes for this exact version.
    if previous.get("version") == version:
        result = dict(previous)
        result["schemaVersion"] = 1
        result["product"] = "ARSAS"
        result["version"] = version
        result["releaseUrl"] = evidence["releaseUrl"]
        result["codeSigning"] = signing_notes(str(signing["status"]), str(signing.get("detail", "")))
        return result

    return generated_notes(evidence, release, previous)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--release", type=Path, required=True)
    parser.add_argument("--notes", type=Path, required=True)
    args = parser.parse_args()

    evidence = read_object(args.evidence, "stable release evidence")
    release = read_object(args.release, "GitHub Release metadata")
    previous = read_object(args.notes, "landing release notes") if args.notes.exists() else {}
    synchronized = synchronize(evidence, release, previous)
    args.notes.write_text(json.dumps(synchronized, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Landing release notes synchronized to ARSAS {synchronized['version']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
