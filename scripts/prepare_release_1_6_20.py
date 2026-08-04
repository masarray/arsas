from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION = "1.6.20"
RELEASE_DATE = "2026-08-04"


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8")


def replace_exact(path: str, old: str, new: str, count: int = 1) -> None:
    text = read(path)
    found = text.count(old)
    if found < count:
        raise SystemExit(f"Expected at least {count} occurrence(s) in {path}, found {found}: {old[:100]!r}")
    write(path, text.replace(old, new, count))


def replace_all(path: str, old: str, new: str) -> None:
    text = read(path)
    if old not in text:
        raise SystemExit(f"Replacement anchor missing in {path}: {old!r}")
    write(path, text.replace(old, new))


def replace_between(path: str, start: str, end: str, replacement: str) -> None:
    text = read(path)
    start_at = text.find(start)
    if start_at < 0:
        raise SystemExit(f"Start marker missing in {path}: {start!r}")
    end_at = text.find(end, start_at + len(start))
    if end_at < 0:
        raise SystemExit(f"End marker missing in {path}: {end!r}")
    write(path, text[:start_at] + replacement + text[end_at:])


# Canonical application version.
for path in ("Directory.Build.props", "ArIED61850Tester.csproj"):
    replace_all(path, "1.6.19", VERSION)
write("VERSION", VERSION + "\n")
replace_exact("CITATION.cff", 'version: "1.6.19"', f'version: "{VERSION}"')
replace_exact("CITATION.cff", 'date-released: "2026-08-01"', f'date-released: "{RELEASE_DATE}"')
replace_exact(
    ".github/workflows/release-windows.yml",
    'default: "1.6.19"',
    f'default: "{VERSION}"',
)

manifest_path = ROOT / ".release/windows.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
manifest["version"] = VERSION
manifest["publicationRequest"] = int(manifest.get("publicationRequest", 0)) + 1
manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

# Promote the complete Unreleased section into the stable release.
release_changelog = f'''## Unreleased

## {VERSION} — {RELEASE_DATE}

### Added

- The Windows portable package is now one self-contained `ARSAS-Windows-x64-Portable.exe` that starts without an installed .NET runtime and without ARSAS requesting UAC elevation.
- Release CI enforces a true one-file publish, loads the ARIEC61850 and managed Npcap dependencies from the bundle, verifies immutable engine provenance, and tests user temporary-directory access before publication.

### Fixed

- IED Explorer and IO List FAT use the shared protection-relay fascia consistently, place LIVE/STOP below the relay artwork, and avoid an icon-level status glow.
- UI contract coverage verifies the shared relay artwork, status-label placement, and absence of the former calculator-style icon.
- Visual QA documentation uses repository-auditable evidence instead of machine-local paths.

### Changed

- The installer continues to use the separately validated multi-file source, while the portable public asset is now a single EXE.
- Release checksums, SPDX SBOM, provenance, attestation inputs, download documentation, and public package metadata now identify the portable EXE instead of the legacy ZIP.
- The portable executable remains subject to AppLocker, WDAC, SmartScreen, antivirus, download-zone, and corporate execution policy. Raw-Ethernet GOOSE and SMV still require an administrator-installed and approved Npcap driver.

'''
replace_between("CHANGELOG.md", "## Unreleased\n", "## 1.6.19", release_changelog)

# README: keep the existing representative screenshot but make the stable package and release claims current.
replace_all("README.md", "[**Portable ZIP**](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip)", "[**Portable single EXE**](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.exe)")
replace_between(
    "README.md",
    "> **Current stable release:",
    "## What changed in v1.6.19",
    f'''> **Current stable release: ARSAS v{VERSION}.** This release adds a true self-contained Windows portable single EXE, preserves the validated installer path, and includes the protection-relay fascia and status-placement QA improvements described below.\n>\n> **Verified release pipeline:** installer + `ARSAS-Windows-x64-Portable.exe` + SHA-256 checksums + SPDX SBOM + provenance and attestations. Public binaries remain unsigned with Authenticode, so SmartScreen warnings are possible.\n\n## What changed in v{VERSION}\n\n- **Real portable single EXE** — copy and run one `ARSAS-Windows-x64-Portable.exe`; no installed .NET runtime and no application-requested UAC elevation are required.\n- **Portable runtime gate** — CI proves that the bundle contains exactly one distributed file, starts successfully, loads ARIEC61850 plus the managed Npcap stack, reads immutable engine provenance, and can use the current user's temporary directory.\n- **Installer preserved** — the normal installer is still built from the separately validated multi-file self-contained publish and passes silent current-user install/uninstall testing.\n- **Relay fascia consistency** — IED Explorer and IO List FAT share the industrial protection-relay artwork, with LIVE/STOP placed below it and no misleading icon glow.\n- **Release trust evidence** — the installer and portable EXE receive public SHA-256 values, SPDX SBOM, provenance metadata, and GitHub artifact attestations.\n- **Honest locked-PC boundary** — portable packaging does not bypass AppLocker, WDAC, SmartScreen, antivirus, or corporate policy; GOOSE and SMV still require an approved administrator-installed Npcap driver.\n\n''',
)
replace_all("README.md", "[Portable ZIP](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip)", "[Portable single EXE](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.exe)")
replace_all("README.md", "Extract and run without installation on a controlled engineering workstation.", "Run the self-contained single EXE without installing ARSAS or .NET on an approved workstation.")
replace_all("README.md", "[ARSAS v1.6.19 release](https://github.com/masarray/arsas/releases/tag/v1.6.19)", f"[ARSAS v{VERSION} release](https://github.com/masarray/arsas/releases/tag/v{VERSION})")
replace_all("README.md", "Install ARSAS or extract the portable ZIP and verify SHA-256.", "Install ARSAS or run the portable single EXE, then verify SHA-256.")

# Stable release notes consumed by the bilingual site builder.
release_notes = {
    "schemaVersion": 1,
    "product": "ARSAS",
    "version": VERSION,
    "title": "True portable single EXE and consistent protection-relay UI",
    "titleId": "Portable single EXE nyata dan UI protection relay yang konsisten",
    "summary": "ARSAS 1.6.20 publishes the Windows portable package as one self-contained EXE, keeps the validated installer path, strengthens runtime and supply-chain checks, and aligns the IED relay fascia across Engineering and IO List FAT.",
    "summaryId": "ARSAS 1.6.20 mempublikasikan paket portable Windows sebagai satu EXE self-contained, mempertahankan jalur installer yang tervalidasi, memperkuat pemeriksaan runtime dan supply chain, serta menyelaraskan fascia relay IED pada Engineering dan IO List FAT.",
    "highlights": [
        "The public portable asset is now one ARSAS-Windows-x64-Portable.exe and does not require an installed .NET runtime or application-requested elevation.",
        "CI enforces exactly one distributed portable file and smoke-tests engine provenance, ARIEC61850, managed Npcap dependencies and user temporary-directory access.",
        "The Windows installer remains based on the separately validated multi-file self-contained publish and passes silent current-user install and uninstall testing.",
        "IED Explorer and IO List FAT now share the protection-relay fascia, with LIVE/STOP below the artwork and no misleading icon-level glow.",
        "Release assets include SHA-256 checksums, SPDX SBOM, provenance metadata and GitHub artifact attestations."
    ],
    "highlightsId": [
        "Asset portable publik kini berupa satu ARSAS-Windows-x64-Portable.exe dan tidak memerlukan .NET terpasang atau elevation yang diminta aplikasi.",
        "CI memastikan hanya ada satu file portable yang didistribusikan serta menguji provenance engine, ARIEC61850, dependency Npcap managed dan akses temporary directory user.",
        "Installer Windows tetap menggunakan publish self-contained multi-file yang divalidasi terpisah dan lulus silent install serta uninstall pada scope current user.",
        "IED Explorer dan IO List FAT kini memakai fascia protection relay yang sama, dengan LIVE/STOP di bawah artwork dan tanpa glow status yang menyesatkan.",
        "Asset release mencakup checksum SHA-256, SPDX SBOM, metadata provenance dan GitHub artifact attestation."
    ],
    "improvements": [
        "Portable publication keeps WPF and packet-capture compatibility by disabling trimming and bundling native/content payloads for extraction to the current user's .NET bundle cache.",
        "The application manifest uses asInvoker so ARSAS itself does not request UAC elevation.",
        "Release checksum, download, documentation and attestation paths identify the portable EXE instead of the legacy ZIP.",
        "UI regression contracts cover the shared relay image, status placement and removal of the former calculator-style icon.",
        "Release metadata is synchronized across the application, changelog, citation, Download Center and bilingual release notes."
    ],
    "improvementsId": [
        "Publikasi portable mempertahankan kompatibilitas WPF dan packet capture dengan mematikan trimming serta membundel payload native/content untuk diekstrak ke .NET bundle cache milik user.",
        "Manifest aplikasi memakai asInvoker sehingga ARSAS sendiri tidak meminta elevation UAC.",
        "Jalur checksum, download, dokumentasi dan attestation release mengidentifikasi portable EXE, bukan ZIP legacy.",
        "Contract regression UI mencakup gambar relay bersama, penempatan status dan penghapusan icon lama bergaya kalkulator.",
        "Metadata release diselaraskan pada aplikasi, changelog, citation, Download Center dan catatan rilis bilingual."
    ],
    "knownLimitations": [
        "Windows x64 is the only packaged desktop platform in the current stable release.",
        "The public binaries are not Authenticode code-signed; Windows SmartScreen may show an unrecognized-publisher warning.",
        "A portable executable cannot bypass AppLocker, Windows Defender Application Control, antivirus, download-zone or corporate execution policy.",
        "The .NET single-file host extracts bundled runtime payloads to the current user's bundle cache on first launch; the user profile and temporary directory must be writable.",
        "Raw-Ethernet GOOSE and Sampled Values workflows require an administrator-installed and approved Npcap driver, suitable capture permission and visibility of the relevant multicast traffic.",
        "Automatic IO List transition testing currently focuses on approved SDI points; analog tolerance and authorized command campaigns remain separate bounded work.",
        "Raw SMV lanes are not calibrated current or voltage measurements until trusted SCL mapping, scaling, synchronization and independent verification are available."
    ],
    "knownLimitationsId": [
        "Windows x64 adalah satu-satunya platform desktop yang dipaketkan pada stable release saat ini.",
        "Binary publik belum ditandatangani dengan Authenticode; Windows SmartScreen dapat menampilkan peringatan unrecognized publisher.",
        "Executable portable tidak dapat melewati AppLocker, Windows Defender Application Control, antivirus, download-zone atau corporate execution policy.",
        "Host single-file .NET mengekstrak payload runtime ke bundle cache user pada first launch; profil user dan temporary directory harus writable.",
        "Workflow raw-Ethernet GOOSE dan Sampled Values memerlukan driver Npcap yang telah dipasang dan disetujui administrator, capture permission yang sesuai dan visibility traffic multicast terkait.",
        "Automatic transition testing IO List saat ini berfokus pada point SDI yang disetujui; analog tolerance dan authorized command campaign tetap merupakan pekerjaan terkontrol yang terpisah.",
        "Raw lane SMV bukan calibrated current atau voltage measurement sampai trusted SCL mapping, scaling, synchronization dan independent verification tersedia."
    ],
    "codeSigning": {
        "status": "unsigned",
        "label": "Not Authenticode-signed",
        "labelId": "Belum ditandatangani dengan Authenticode",
        "detail": "The current public Windows installer and portable EXE do not carry a commercial Authenticode publisher signature. Verify the published SHA-256 value before use. SmartScreen warnings are therefore possible and are not hidden from users.",
        "detailId": "Installer Windows dan portable EXE publik saat ini belum memiliki commercial Authenticode publisher signature. Verifikasi nilai SHA-256 yang dipublikasikan sebelum digunakan. Karena itu peringatan SmartScreen masih mungkin muncul dan status ini tidak disembunyikan dari user."
    },
    "screenshot": {
        "src": "assets/screenshots/arsas-live-values.webp",
        "width": 1507,
        "height": 893,
        "alt": "Representative ARSAS 1.6.20 Engineering workspace with attributable IED values, quality and timestamps",
        "altId": "Representasi Engineering workspace ARSAS 1.6.20 dengan value IED, quality dan timestamp yang attributable",
        "caption": "Representative Engineering workspace for ARSAS 1.6.20. This release changes Windows packaging and relay-fascia consistency without expanding unsupported protocol claims.",
        "captionId": "Representasi Engineering workspace ARSAS 1.6.20. Release ini mengubah packaging Windows dan konsistensi fascia relay tanpa memperluas klaim protocol yang belum didukung."
    },
    "issuesUrl": "https://github.com/masarray/arsas/issues/new/choose",
    "releaseUrl": f"https://github.com/masarray/arsas/releases/tag/v{VERSION}"
}
write("landing/release-notes.json", json.dumps(release_notes, indent=2, ensure_ascii=False) + "\n")

# Shared download calls to action.
replace_all("landing/partials/download-cta.html", "choose the portable ZIP for a controlled folder deployment", "choose the portable single EXE for an approved no-install deployment")
replace_all("landing/partials/download-cta-id.html", "pilih portable ZIP untuk deployment berbasis folder yang terkontrol", "pilih portable single EXE untuk deployment tanpa instalasi yang disetujui")

# English Download Center.
download_replacements = {
    "installer or portable ZIP": "installer or portable single EXE",
    "the portable ZIP for a controlled folder-based deployment": "the portable single EXE for an approved no-install deployment",
    "Download portable ZIP": "Download portable single EXE",
    '<span class="kicker">Folder-based</span><h2>Portable ZIP</h2><p>Best for approved test laptops, temporary evaluation or environments where installation is not preferred. Extract to a writable local folder and update by replacing the package.</p>': '<span class="kicker">Single file</span><h2>Portable single EXE</h2><p>Best for approved test laptops, temporary evaluation or environments where installation is not preferred. Copy the EXE to a user-writable folder and run it without installing ARSAS or .NET.</p>',
    "ARSAS-Windows-x64-Portable.zip": "ARSAS-Windows-x64-Portable.exe",
    "Portable ZIP</strong>": "Portable single EXE</strong>",
    "Controlled test folder or temporary evaluation": "Approved no-install workstation or temporary evaluation",
    "Extract and run from a writable local folder": "Copy and run one EXE from a writable local folder",
    "Manual package replacement recommended": "Manual EXE replacement recommended",
    "Installer, portable ZIP, publish date, file size, SHA-256 and honest Authenticode status.": "Installer, portable single EXE, publish date, file size, SHA-256, SPDX SBOM, provenance and honest Authenticode status.",
    "Future release workflow</strong><p>Releases built after the P3 supply-chain workflow generate": "Current release workflow</strong><p>The stable Windows release generates"
}
for old, new in download_replacements.items():
    replace_all("landing/templates/download.html", old, new)

# Indonesian Download Center.
unduh_replacements = {
    "installer atau portable ZIP": "installer atau portable single EXE",
    "portable ZIP untuk deployment berbasis folder": "portable single EXE untuk deployment tanpa instalasi",
    "Unduh portable ZIP": "Unduh portable single EXE",
    '<span class="kicker">Berbasis folder</span><h2>Portable ZIP</h2><p>Cocok untuk laptop pengujian, evaluasi sementara atau lingkungan tanpa instalasi. Ekstrak ke folder lokal writable dan update dengan mengganti paket.</p>': '<span class="kicker">Satu file</span><h2>Portable single EXE</h2><p>Cocok untuk laptop pengujian, evaluasi sementara atau lingkungan tanpa instalasi. Salin EXE ke folder user yang writable lalu jalankan tanpa menginstal ARSAS atau .NET.</p>',
    "ARSAS-Windows-x64-Portable.zip": "ARSAS-Windows-x64-Portable.exe",
    "Portable ZIP</strong>": "Portable single EXE</strong>",
    "Folder test terkontrol atau evaluasi": "Workstation tanpa instalasi yang disetujui atau evaluasi",
    "Ekstrak dan jalankan dari folder writable": "Salin dan jalankan satu EXE dari folder writable",
    "Penggantian paket manual": "Penggantian EXE manual",
    "Installer, portable ZIP, tanggal publish, ukuran, SHA-256 dan status Authenticode yang jujur.": "Installer, portable single EXE, tanggal publish, ukuran, SHA-256, SPDX SBOM, provenance dan status Authenticode yang jujur.",
    "Workflow release berikutnya</strong><p>Release yang dibangun setelah workflow P3 menghasilkan": "Workflow release saat ini</strong><p>Stable release Windows menghasilkan"
}
for old, new in unduh_replacements.items():
    replace_all("landing/templates/unduh.html", old, new)

replace_all("landing/templates/quick-start.html", "installer or portable ZIP", "installer or portable single EXE")
replace_all("landing/templates/panduan-mulai-arsas.html", "installer atau Portable ZIP", "installer atau portable single EXE")

# Release-critical files must no longer promise the removed ZIP package.
critical = [
    "README.md",
    "landing/partials/download-cta.html",
    "landing/partials/download-cta-id.html",
    "landing/templates/download.html",
    "landing/templates/unduh.html",
    "landing/templates/quick-start.html",
    "landing/templates/panduan-mulai-arsas.html",
    "landing/release-notes.json",
]
for path in critical:
    text = read(path)
    for forbidden in ("Portable.zip", "Portable ZIP", "portable ZIP"):
        if forbidden in text:
            raise SystemExit(f"Legacy portable ZIP reference remains in {path}: {forbidden}")

print(f"Prepared ARSAS {VERSION} release metadata and single-EXE documentation.")
