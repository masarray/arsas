#!/usr/bin/env python3
"""Validate the rendered ARSAS product website, localization and release trust."""

from __future__ import annotations

import json
import re
import struct
import sys
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlparse

CANONICAL_ROOT = "https://masarray.github.io/arsas/"
INSTALLER = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe"
PORTABLE = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.exe"
CHECKSUMS = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SHA256SUMS.txt"
EXPECTED_NAV = {"overview", "learn", "capabilities", "solutions", "guides", "download"}
GUIDES = {
    "reporting-silent.html", "brcb-vs-urcb.html", "rcb-reserved.html", "empty-dataset.html",
    "port-102-connection-failed.html", "comtrade-download.html", "goose-sequence.html",
    "cid-rejected.html", "live-model-vs-scl.html", "direct-vs-sbo.html", "commandtermination-addcause.html",
}
PAIRS = {
    "index.html": "id.html", "download.html": "unduh.html", "release-notes.html": "catatan-rilis.html",
    "quick-start.html": "panduan-mulai-arsas.html", "learning-center.html": "pusat-belajar-iec61850.html",
    "what-is-iec61850.html": "apa-itu-iec61850.html", "connect-ied-ip-arsas.html": "cara-hubungkan-ied-ip-arsas.html",
    "faq.html": "faq-arsas.html", "compatibility.html": "bukti-kompatibilitas.html", "demo.html": "demo-arsas.html",
    "guides.html": "panduan.html", "mms-client.html": "mms-client-iec61850.html",
    "smart-reporting.html": "smart-reporting-iec61850.html", "goose-analyzer.html": "analyzer-goose-iec61850.html",
    "file-transfer.html": "transfer-file-comtrade-iec61850.html", "scl-workspace.html": "workspace-scl-iec61850.html",
    "io-list-fat-evidence.html": "bukti-fat-iolist-iec61850.html",
    "fat-testing.html": "pengujian-fat-iec61850.html", "sat-testing.html": "pengujian-sat-iec61850.html",
    "commissioning.html": "commissioning-iec61850.html", "multi-vendor-integration.html": "integrasi-multi-vendor-iec61850.html",
}
INDEXNOW_FILE = "arsas-iec61850-20260720-6f4a9d2c8b.txt"


class Audit(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.lang = ""
        self.title = ""
        self.in_title = False
        self.h1 = 0
        self.description = ""
        self.body_page = ""
        self.meta: dict[str, str] = {}
        self.refs: list[str] = []
        self.images: list[dict[str, str | None]] = []
        self.alternates: dict[str, str] = {}
        self.nav: set[str] = set()

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "html": self.lang = values.get("lang") or ""
        if tag == "title": self.in_title = True
        if tag == "h1": self.h1 += 1
        if tag == "body": self.body_page = values.get("data-page") or ""
        if tag == "meta":
            key = (values.get("name") or values.get("property") or "").lower()
            value = values.get("content") or ""
            if key: self.meta[key] = value
            if key == "description": self.description = value
        if tag == "a" and values.get("data-nav-page"): self.nav.add(values.get("data-nav-page") or "")
        if tag == "link" and values.get("rel") == "alternate" and values.get("hreflang"): self.alternates[values.get("hreflang") or ""] = values.get("href") or ""
        for key in ("href", "src"):
            if values.get(key): self.refs.append(values[key] or "")
        if tag == "img": self.images.append(values)

    def handle_endtag(self, tag: str) -> None:
        if tag == "title": self.in_title = False

    def handle_data(self, data: str) -> None:
        if self.in_title: self.title += data


def png_size(path: Path) -> tuple[int, int]:
    data = path.read_bytes()[:24]
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n": raise ValueError("not a PNG")
    return struct.unpack(">II", data[16:24])


def page_url(path: str) -> str:
    return CANONICAL_ROOT if path == "index.html" else CANONICAL_ROOT + path


def local_target(site: Path, page: Path, reference: str) -> Path | None:
    clean = reference.split("#", 1)[0].split("?", 1)[0]
    parsed = urlparse(clean)
    if not clean or parsed.scheme or parsed.netloc or clean.startswith("#"): return None
    return (page.parent / clean).resolve()


def main() -> int:
    site = Path(sys.argv[1] if len(sys.argv) > 1 else "_site").resolve()
    errors: list[str] = []
    try:
        registry = json.loads((site / "site.json").read_text(encoding="utf-8"))
        info = json.loads((site / "build-info.json").read_text(encoding="utf-8"))
        latest = json.loads((site / "latest.json").read_text(encoding="utf-8"))
        notes = json.loads((site / "release-notes.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        print(f"ARSAS rendered validation failed:\n- core JSON: {exc}", file=sys.stderr)
        return 1

    entries = registry.get("pages", []) if isinstance(registry, dict) else []
    expected_pages = [str(item.get("path") or "index.html") for item in entries if isinstance(item, dict)]
    if len(expected_pages) != 62 or len(expected_pages) != len(set(expected_pages)): errors.append("site registry must contain 62 unique pages")
    if info.get("schemaVersion") != 3 or info.get("pages") != expected_pages: errors.append("build-info page registry does not match site.json")
    if info.get("languages") != ["en", "id"]: errors.append("build-info languages must be en and id")
    if info.get("repository") != "https://github.com/masarray/arsas": errors.append("build-info repository is invalid")
    if info.get("indexNowKeyLocation") != CANONICAL_ROOT + INDEXNOW_FILE: errors.append("build-info IndexNow location is invalid")
    if latest.get("version") != notes.get("version") or latest.get("channel") != "stable": errors.append("stable release JSON is inconsistent")
    stable_source = str(latest.get("sourceCommit", ""))
    if not re.fullmatch(r"[0-9a-f]{40}", stable_source): errors.append("stable release source commit is invalid")
    if not re.fullmatch(r"v\d+\.\d+\.\d+", str(latest.get("tag", ""))): errors.append("stable release tag is invalid")
    for key, url in (("installer", INSTALLER), ("portable", PORTABLE)):
        item = latest.get(key)
        if not isinstance(item, dict) or item.get("url") != url or not re.fullmatch(r"[0-9a-fA-F]{64}", str(item.get("sha256", ""))): errors.append(f"latest.json {key} evidence is invalid")
    if not isinstance(latest.get("checksums"), dict) or latest["checksums"].get("url") != CHECKSUMS: errors.append("latest.json checksum URL is invalid")

    entry_map = {str(item.get("path") or "index.html"): item for item in entries if isinstance(item, dict)}
    rendered: dict[str, Audit] = {}
    for name in expected_pages:
        page = site / name
        if not page.is_file():
            errors.append(f"missing rendered page {name}")
            continue
        text = page.read_text(encoding="utf-8")
        if "{{" in text: errors.append(f"{name}: unresolved template token")
        audit = Audit(); audit.feed(text); rendered[name] = audit
        if not audit.title.strip() or audit.h1 != 1 or not 60 <= len(audit.description) <= 260: errors.append(f"{name}: metadata or h1 contract failed")
        language = str(entry_map[name].get("language", "en"))
        if audit.lang != language: errors.append(f"{name}: html language must be {language}")
        if not audit.body_page: errors.append(f"{name}: body data-page is missing")
        for image in audit.images:
            src = image.get("src") or ""
            if image.get("alt") is None or not image.get("width") or not image.get("height"): errors.append(f"{name}: incomplete image metadata {src}")
            target = local_target(site, page, src)
            if target is not None and not target.is_file(): errors.append(f"{name}: missing image {src}")
        for reference in audit.refs:
            target = local_target(site, page, reference)
            if target is not None and not target.exists(): errors.append(f"{name}: broken local reference {reference}")
        if name != "404.html" and EXPECTED_NAV - audit.nav: errors.append(f"{name}: shared navigation is incomplete")

    for english, indonesian in PAIRS.items():
        expected = {"en": page_url(english), "id": page_url(indonesian), "x-default": page_url(english)}
        for page in (english, indonesian):
            audit = rendered.get(page)
            if audit and audit.alternates != expected: errors.append(f"{page}: reciprocal hreflang set is invalid")
    for home, expected_locale, expected_alternate in (("index.html", "en_US", "id_ID"), ("id.html", "id_ID", "en_US")):
        audit = rendered.get(home)
        if not audit: continue
        social_url = CANONICAL_ROOT + "assets/social-card.png"
        expected_social_meta = {
            "og:locale": expected_locale,
            "og:locale:alternate": expected_alternate,
            "og:image": social_url,
            "og:image:secure_url": social_url,
            "og:image:type": "image/png",
            "twitter:card": "summary_large_image",
            "twitter:image": social_url,
        }
        for key, value in expected_social_meta.items():
            if audit.meta.get(key) != value: errors.append(f"{home}: invalid {key}")
        if not audit.meta.get("twitter:image:alt"): errors.append(f"{home}: missing twitter:image:alt")
        screenshot_images = [
            image for image in audit.images
            if str(image.get("src") or "").startswith("assets/screenshots/")
        ]
        if len(screenshot_images) != 10:
            errors.append(f"{home}: expected hero, three Quick Start screenshots and six curated product screenshots, found {len(screenshot_images)}")
        if any(image.get("src") == "assets/arsas-substation-context.webp" for image in audit.images):
            errors.append(f"{home}: compact homepage must not load decorative substation media")
        if "home.css" not in audit.refs:
            errors.append(f"{home}: scoped home.css is missing")
        home_text = (site / home).read_text(encoding="utf-8")
        search_contract = (
            ("IEC 61850 tester", "learning-center.html", "connect-ied-ip-arsas.html", "fat-testing.html", "sat-testing.html", "multi-vendor-integration.html")
            if home == "index.html" else
            ("Tester IEC 61850", "pusat-belajar-iec61850.html", "cara-hubungkan-ied-ip-arsas.html", "pengujian-fat-iec61850.html", "pengujian-sat-iec61850.html", "integrasi-multi-vendor-iec61850.html")
        )
        for value in search_contract:
            if value not in home_text: errors.append(f"{home}: missing search-to-engineering contract value {value}")
        premium_contract = (
            ("ARSAS is a free Windows", "Start here", "Download for Windows", "Real product evidence")
            if home == "index.html" else
            ("ARSAS adalah tester IEC 61850 Windows gratis", "Mulai di sini", "Unduh untuk Windows", "Evidence produk nyata")
        )
        for value in premium_contract:
            if value not in home_text: errors.append(f"{home}: missing premium homepage contract value {value}")
        onboarding_contract = (
            ("I have a live relay or IED", "Recommended first connection", "I have an engineering file", "Substation Configuration Language", "ICD", "CID", "IID", "SCD", "Open the complete Quick Start")
            if home == "index.html" else
            ("Saya punya relay atau IED live", "Disarankan untuk koneksi pertama", "Saya punya file engineering", "Substation Configuration Language", "ICD", "CID", "IID", "SCD", "Buka Quick Start lengkap")
        )
        for value in onboarding_contract:
            if value not in home_text: errors.append(f"{home}: missing guided-onboarding contract value {value}")
        capability_contract = (
            ("One IED context, six engineering jobs.", "Discover the real device", "Evidence collector", "How acquired?", "Open and inspectable.")
            if home == "index.html" else
            ("Satu konteks IED, enam pekerjaan engineering.", "Temukan apa yang benar-benar diekspos device", "Evidence collector", "Diperoleh bagaimana?", "Open dan dapat diperiksa.")
        )
        for value in capability_contract:
            if value not in home_text: errors.append(f"{home}: missing beginner-first capability contract value {value}")
        discovery_scl_contract = (
            ("Discovery → engineering model", "IED IP", "Live discovery", "Observed model", "Configured SCL intent", "bounded device baseline")
            if home == "index.html" else
            ("Discovery → model engineering", "IP IED", "Live discovery", "Observed model", "Configured SCL intent", "baseline device yang bounded")
        )
        for value in discovery_scl_contract:
            if value not in home_text: errors.append(f"{home}: missing discovery-to-SCL contract value {value}")
        trust_contract = (
            ('data-trust-architecture="true"', "Trust the exact release because its evidence can be inspected.", "Open source is not automatic correctness.", stable_source)
            if home == "index.html" else
            ('data-trust-architecture="true"', "Percaya pada release exact karena evidence-nya dapat diperiksa.", "Open source bukan jaminan correctness otomatis.", stable_source)
        )
        for value in trust_contract:
            if value not in home_text: errors.append(f"{home}: missing R5.6 open-source reliability value {value}")
        for ambiguous_ip in ("approved relay IP", "approved IED IP", "Connect by approved IP address", "alamat IP relay yang disetujui", "IP IED yang disetujui", "alamat IP yang disetujui"):
            if ambiguous_ip in home_text: errors.append(f"{home}: ambiguous endpoint-authority wording remains: {ambiguous_ip}")
        for stale_section in ("Go deeper when you are ready", "Masuk lebih dalam saat siap"):
            if stale_section in home_text: errors.append(f"{home}: redundant homepage depth section remains: {stale_section}")
        overview_flow = (
            "home-quick-start-section",
            "home-paths-section",
            "home-capabilities",
            "discovery-scl-showcase",
            "home-evidence",
            "release-trust-section",
        )
        overview_positions = [home_text.find(marker) for marker in overview_flow]
        if any(position < 0 for position in overview_positions) or overview_positions != sorted(overview_positions):
            errors.append(f"{home}: beginner-to-evidence homepage flow is missing or out of order")
        if "home-workflows" in home_text:
            errors.append(f"{home}: duplicated progressive-engineering overview layer must stay removed")
        if "assets/fonts/Inter-Regular.ttf" not in home_text:
            errors.append(f"{home}: embedded Inter preload is missing")
        if home == "id.html":
            for stale in ("Have the software?", "Connect an approved IED", "Follow the first connection"):
                if stale in home_text: errors.append(f"{home}: stale English homepage localization remains: {stale}")
    technical_review_text = (site / "technical-review.html").read_text(encoding="utf-8") if (site / "technical-review.html").is_file() else ""
    for value in ('data-trust-architecture="true"', "SPDX SBOM", "CI regression evidence", stable_source, "Not a conformance certificate"):
        if value not in technical_review_text: errors.append(f"technical-review.html: missing R5.6 reliability value {value}")
    for page in ("download.html", "unduh.html", "release-notes.html", "catatan-rilis.html"):
        release_text = (site / page).read_text(encoding="utf-8") if (site / page).is_file() else ""
        for value in (stable_source, str(latest.get("tag", "")), "ARSAS-Windows-x64-SBOM.spdx.json", "ARSAS-Windows-x64-PROVENANCE.json", "reproducible build"):
            if value not in release_text: errors.append(f"{page}: missing rendered exact-release trust value {value}")
    features_text = (site / "features.html").read_text(encoding="utf-8") if (site / "features.html").is_file() else ""
    for value in ("Six capability domains", "Discover &amp; Model", "Monitor &amp; Events", "Inspect Communications", "Files &amp; Disturbance", "Evidence &amp; Engineering"):
        if value not in features_text: errors.append(f"features.html: missing R5 capability-domain contract value {value}")
    for forbidden in ("approved IED IP address", "approved IP address", "Current source and published release"):
        if forbidden.lower() in features_text.lower():
            errors.append(f"features.html: final-audit duplication/authority wording returned: {forbidden}")
    for page, contract in (
        ("scl-workspace.html", ("From IP to SCL in four steps", "Discovery builds observed device evidence before export begins.", "Generate IID Edition 2 or ICD Edition 1", 'id="source-boundary"')),
        ("workspace-scl-iec61850.html", ("Dari IP ke SCL dalam empat langkah", "Discovery membangun observed device evidence sebelum export dimulai.", "Generate IID Edition 2 atau ICD Edition 1", 'id="source-boundary"')),
        ("mms-client.html", ("Continue from discovery to SCL", "authorized engineering network")),
        ("mms-client-iec61850.html", ("Lanjut dari discovery ke SCL", "network engineering yang berwenang")),
    ):
        page_text = (site / page).read_text(encoding="utf-8") if (site / page).is_file() else ""
        for value in contract:
            if value not in page_text: errors.append(f"{page}: missing R5.2 discovery/SCL contract value {value}")
    evidence_contract_pages = {
        "mms-client.html": ("Evidence contract", "Which IED?", "How acquired?", "No invented certainty."),
        "smart-reporting.html": ("Evidence contract", "Which IED?", "How acquired?", "No invented certainty."),
        "goose-analyzer.html": ("Evidence contract", "Which IED?", "How acquired?", "No invented certainty."),
        "file-transfer.html": ("Evidence contract", "Which IED?", "How acquired?", "No invented certainty."),
        "scl-workspace.html": ("Evidence contract", "Which IED?", "How acquired?", "No invented certainty."),
        "control.html": ("Evidence contract", "Which IED?", "How acquired?", "No invented certainty."),
        "mms-client-iec61850.html": ("Kontrak evidence", "Dari IED mana?", "Diperoleh bagaimana?", "Tidak mengarang kepastian."),
        "smart-reporting-iec61850.html": ("Kontrak evidence", "Dari IED mana?", "Diperoleh bagaimana?", "Tidak mengarang kepastian."),
        "analyzer-goose-iec61850.html": ("Kontrak evidence", "Dari IED mana?", "Diperoleh bagaimana?", "Tidak mengarang kepastian."),
        "transfer-file-comtrade-iec61850.html": ("Kontrak evidence", "Dari IED mana?", "Diperoleh bagaimana?", "Tidak mengarang kepastian."),
        "workspace-scl-iec61850.html": ("Kontrak evidence", "Dari IED mana?", "Diperoleh bagaimana?", "Tidak mengarang kepastian."),
    }
    for page, contract in evidence_contract_pages.items():
        page_text = (site / page).read_text(encoding="utf-8") if (site / page).is_file() else ""
        for value in contract:
            if value not in page_text: errors.append(f"{page}: missing R5.3 evidence-contract value {value}")
    investigation_contract_pages = {
        "features.html": ("Investigation workflow", "Network evidence", "Event / SOE", "Fault record", "COMTRADE", "Engineering timeline", "Correlation is not automatic causation proof."),
        "goose-analyzer.html": ("Investigation workflow", "Network evidence", "Event / SOE", "Fault record", "COMTRADE", "Engineering timeline", "Correlation is not automatic causation proof."),
        "file-transfer.html": ("Investigation workflow", "Network evidence", "Event / SOE", "Fault record", "COMTRADE", "Engineering timeline", "Correlation is not automatic causation proof."),
        "multi-ied-monitoring.html": ("Investigation workflow", "Network evidence", "Event / SOE", "Fault record", "COMTRADE", "Engineering timeline", "Correlation is not automatic causation proof."),
        "demo.html": ("Investigation workflow", "Network evidence", "Event / SOE", "Fault record", "COMTRADE", "Engineering timeline", "Correlation is not automatic causation proof."),
        "analyzer-goose-iec61850.html": ("Workflow investigasi", "Evidence network", "Event / SOE", "Fault record", "COMTRADE", "Timeline engineering", "Korelasi bukan otomatis bukti sebab-akibat."),
        "transfer-file-comtrade-iec61850.html": ("Workflow investigasi", "Evidence network", "Event / SOE", "Fault record", "COMTRADE", "Timeline engineering", "Korelasi bukan otomatis bukti sebab-akibat."),
        "demo-arsas.html": ("Workflow investigasi", "Evidence network", "Event / SOE", "Fault record", "COMTRADE", "Timeline engineering", "Korelasi bukan otomatis bukti sebab-akibat."),
    }
    for page, contract in investigation_contract_pages.items():
        page_text = (site / page).read_text(encoding="utf-8") if (site / page).is_file() else ""
        if 'data-investigation-path="true"' not in page_text:
            errors.append(f"{page}: missing R5.5 investigation path marker")
        for value in contract:
            if value not in page_text: errors.append(f"{page}: missing R5.5 investigation-path value {value}")
    for quick, contract in (
        ("quick-start.html", ("Beginner Quick Start", "Engineering Quick Start", "authorized test network", "Static DataSet", "Select Signals")),
        ("panduan-mulai-arsas.html", ("Quick Start pemula", "Quick Start Engineering", "network test yang berwenang", "Static DataSet", "Select Signals")),
    ):
        quick_text = (site / quick).read_text(encoding="utf-8") if (site / quick).is_file() else ""
        for value in contract:
            if value not in quick_text: errors.append(f"{quick}: missing beginner-to-engineering quick-start contract value {value}")
        for ambiguous_ip in ("approved relay IP", "approved IP address", "alamat IP relay yang disetujui", "alamat IP yang disetujui"):
            if ambiguous_ip in quick_text: errors.append(f"{quick}: ambiguous endpoint-authority wording remains: {ambiguous_ip}")
    for page in ("compatibility.html", "bukti-kompatibilitas.html"):
        matrix_text = (site / page).read_text(encoding="utf-8") if (site / page).is_file() else ""
        for value in ('data-evidence-matrix="true"', 'field-profile-a-file-service:mmsAssociation:observed', 'field-profile-b-rcb-export:selectedRcbExport:verified', "device-evidence.json"):
            if value not in matrix_text: errors.append(f"{page}: missing rendered interoperability matrix value {value}")
    if not GUIDES.issubset(set(expected_pages)): errors.append("troubleshooting guides are missing from the build")

    sitemap = site / "sitemap.xml"
    try:
        tree = ET.parse(sitemap)
        ns = {"sm": "http://www.sitemaps.org/schemas/sitemap/0.9", "xhtml": "http://www.w3.org/1999/xhtml"}
        urls = tree.getroot().findall("sm:url", ns)
        locations = [(node.findtext("sm:loc", default="", namespaces=ns) or "").strip() for node in urls]
        expected_indexable = [name for name in expected_pages if entry_map[name].get("index", True) is not False]
        expected_locations = [page_url(name) for name in expected_indexable]
        if locations != expected_locations: errors.append("sitemap URLs do not match indexable registry order")
        if len(locations) != 61: errors.append(f"sitemap must contain 61 URLs, found {len(locations)}")
        if page_url("404.html") in locations: errors.append("404 page must not be in sitemap")
    except (OSError, ET.ParseError) as exc:
        errors.append(f"sitemap.xml: {exc}")

    for required in (
        "assets/app-icon.png", "assets/social-card.png", "assets/arsas-substation-context.webp", "assets/screenshots/arsas-first-launch.webp", "assets/screenshots/arsas-overview-v1.6.19.webp",
        "assets/screenshots/arsas-quick-start-choose-source-v1.6.40.webp", "assets/screenshots/arsas-quick-start-discover-ip-v1.6.40.webp", "assets/screenshots/arsas-quick-start-monitor-control-v1.6.40.webp",
        "assets/fonts/Inter-Regular.ttf", "assets/fonts/Inter-Medium.ttf", "assets/fonts/Inter-SemiBold.ttf", "assets/fonts/Inter-Bold.ttf", "assets/fonts/Inter-LICENSE.txt",
        "assets/screenshots/arsas-multi-ied.webp", "assets/screenshots/arsas-live-values.webp",
        "assets/screenshots/arsas-event-log.webp", "assets/screenshots/arsas-goose.webp",
        "assets/screenshots/arsas-diagnostics.webp", "assets/screenshots/arsas-rcb-scl-export.webp",
        "device-evidence.json", "adoption.css", "guide-filter.js", "demo.js", INDEXNOW_FILE,
    ):
        if not (site / required).is_file(): errors.append(f"missing output {required}")
    try:
        width, height = png_size(site / "assets/app-icon.png")
        if width != height or width < 256: errors.append("rendered app icon is invalid")
    except (OSError, ValueError) as exc: errors.append(f"app icon: {exc}")
    try:
        if png_size(site / "assets/social-card.png") != (1200, 630): errors.append("rendered social card must be 1200x630")
    except (OSError, ValueError) as exc: errors.append(f"social card: {exc}")

    polish = (site / "polish.css").read_text(encoding="utf-8") if (site / "polish.css").is_file() else ""
    for value in ('font-family: "Inter"', 'Inter-Regular.ttf', 'Inter-Medium.ttf', 'Inter-SemiBold.ttf', 'Inter-Bold.ttf', "font-display: swap"):
        if value not in polish: errors.append(f"rendered polish.css missing embedded Inter contract value {value}")
    combined = "\n".join((site / name).read_text(encoding="utf-8") for name in expected_pages if (site / name).is_file())
    for value in ('href="http://', 'src="http://', "raw.githubusercontent.com/masarray/arsas/main/Assets/screenshot", '<meta name="keywords"', "fonts.googleapis.com", "fonts.gstatic.com", "rsms.me"):
        if value in combined: errors.append(f"forbidden public value remains: {value}")
    for value in (INSTALLER, PORTABLE, CHECKSUMS, "Ari Sulistiono", "GPL-3.0-or-later", "learning-center.html", "what-is-iec61850.html", "connect-ied-ip-arsas.html", "io-list-fat-evidence.html", ".arsas"):
        if value not in combined: errors.append(f"public site missing trust value {value}")

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS rendered validation failed:", file=sys.stderr)
        for error in errors: print(f"- {error}", file=sys.stderr)
        return 1
    print("ARSAS rendered validation passed: 62 pages, 61 sitemap URLs, 21 localized pages, learning paths, release trust, links and media.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
