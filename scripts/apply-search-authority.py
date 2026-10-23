#!/usr/bin/env python3
"""Validate and apply the ARSAS contextual search-authority graph to a built site."""

from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from html import escape
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_GRAPH = ROOT / "landing" / "search-authority.json"
GUIDE_ROLES = {"guide"}
DISCOVERY_ROLES = {"capability", "solution"}
ALLOWED_ROLES = {"capability", "solution", "guide", "hub", "authority", "learning", "tutorial"}
INDONESIAN_PAGES = {
    "id.html", "unduh.html", "catatan-rilis.html", "panduan-mulai-arsas.html",
    "pusat-belajar-iec61850.html", "apa-itu-iec61850.html", "cara-hubungkan-ied-ip-arsas.html",
    "faq-arsas.html", "bukti-kompatibilitas.html", "demo-arsas.html", "panduan.html",
    "mms-client-iec61850.html", "smart-reporting-iec61850.html", "analyzer-goose-iec61850.html",
    "transfer-file-comtrade-iec61850.html", "workspace-scl-iec61850.html",
    "bukti-fat-iolist-iec61850.html", "pengujian-fat-iec61850.html", "pengujian-sat-iec61850.html",
    "commissioning-iec61850.html", "integrasi-multi-vendor-iec61850.html",
}


def load_object(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise SystemExit(f"Cannot read {label}: {exc}") from exc
    if not isinstance(value, dict):
        raise SystemExit(f"{label} must contain a JSON object")
    return value


def registry(site: Path) -> dict[str, dict[str, Any]]:
    config = load_object(site / "site.json", "built site registry")
    pages = config.get("pages")
    if not isinstance(pages, list):
        raise SystemExit("Built site registry has no pages list")
    result: dict[str, dict[str, Any]] = {}
    for item in pages:
        if not isinstance(item, dict) or not isinstance(item.get("path"), str):
            raise SystemExit("Built site registry contains an invalid page entry")
        path = item["path"] or "index.html"
        result[path] = item
    return result


def text(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise SystemExit(f"Search authority graph has invalid {label}")
    return value.strip()


def localized(node: dict[str, Any], key: str, language: str) -> str:
    values = node.get(key)
    if not isinstance(values, dict):
        raise SystemExit(f"Search authority node is missing {key}")
    return text(values.get(language) or values.get("en"), f"{key}.{language}")


def validate_graph(graph: dict[str, Any], entries: dict[str, dict[str, Any]]) -> tuple[dict[str, dict[str, Any]], dict[str, list[str]], dict[str, int]]:
    if graph.get("schemaVersion") != 1:
        raise SystemExit("Search authority graph schemaVersion must be 1")
    clusters = graph.get("clusters")
    nodes = graph.get("nodes")
    pages = graph.get("pages")
    if not isinstance(clusters, dict) or not clusters:
        raise SystemExit("Search authority graph must define clusters")
    if not isinstance(nodes, dict) or not nodes:
        raise SystemExit("Search authority graph must define nodes")
    if not isinstance(pages, dict) or not pages:
        raise SystemExit("Search authority graph must define page relationships")

    indexed = {path for path, item in entries.items() if item.get("index", True) is not False}
    for cluster, value in clusters.items():
        text(cluster, "cluster key")
        if not isinstance(value, dict):
            raise SystemExit(f"Search authority cluster {cluster} must be an object")
        labels = value.get("label")
        if not isinstance(labels, dict):
            raise SystemExit(f"Search authority cluster {cluster} is missing labels")
        text(labels.get("en"), f"clusters.{cluster}.label.en")
        text(labels.get("id"), f"clusters.{cluster}.label.id")

    normalized_nodes: dict[str, dict[str, Any]] = {}
    for path, node in nodes.items():
        if path not in indexed:
            raise SystemExit(f"Search authority node is not an indexable page: {path}")
        if not isinstance(node, dict):
            raise SystemExit(f"Search authority node {path} must be an object")
        cluster = text(node.get("cluster"), f"nodes.{path}.cluster")
        if cluster not in clusters:
            raise SystemExit(f"Search authority node {path} uses unknown cluster {cluster}")
        role = text(node.get("role"), f"nodes.{path}.role")
        if role not in ALLOWED_ROLES:
            raise SystemExit(f"Search authority node {path} has invalid role {role}")
        localized(node, "label", "en")
        localized(node, "label", "id")
        localized(node, "summary", "en")
        localized(node, "summary", "id")
        text(node.get("primaryQuery"), f"nodes.{path}.primaryQuery")
        normalized_nodes[path] = node

    normalized_pages: dict[str, list[str]] = {}
    inbound: dict[str, int] = defaultdict(int)
    discovery_inbound: dict[str, int] = defaultdict(int)
    for source, raw_targets in pages.items():
        if source not in normalized_nodes:
            raise SystemExit(f"Search authority source has no node metadata: {source}")
        if not isinstance(raw_targets, list) or not 2 <= len(raw_targets) <= 5:
            raise SystemExit(f"Search authority source {source} must have two to five related pages")
        targets = [text(item, f"pages.{source}") for item in raw_targets]
        if len(targets) != len(set(targets)):
            raise SystemExit(f"Search authority source {source} contains duplicate targets")
        if source in targets:
            raise SystemExit(f"Search authority source {source} links to itself")
        for target in targets:
            if target not in normalized_nodes:
                raise SystemExit(f"Search authority target has no node metadata: {target}")
            inbound[target] += 1
            if normalized_nodes[source]["role"] in DISCOVERY_ROLES:
                discovery_inbound[target] += 1
        normalized_pages[source] = targets

    guide_paths = {
        path for path, item in entries.items()
        if item.get("contentType") == "guide"
    }
    missing_nodes = sorted(guide_paths - set(normalized_nodes))
    if missing_nodes:
        raise SystemExit("Troubleshooting guides missing from authority graph: " + ", ".join(missing_nodes))
    missing_discovery = sorted(path for path in guide_paths if discovery_inbound[path] < 1)
    if missing_discovery:
        raise SystemExit("Guides lack capability/solution inbound paths: " + ", ".join(missing_discovery))

    stats = {
        "clusterCount": len(clusters),
        "nodeCount": len(normalized_nodes),
        "mappedPageCount": len(normalized_pages),
        "guideCount": len(guide_paths),
        "guidesWithDiscoveryInbound": sum(1 for path in guide_paths if discovery_inbound[path] >= 1),
        "relationshipCount": sum(len(items) for items in normalized_pages.values()),
    }
    return normalized_nodes, normalized_pages, stats


def target_attributes(source_language: str, target_language: str) -> str:
    if source_language == target_language:
        return ""
    return f' lang="{escape(target_language)}" hreflang="{escape(target_language)}"'


def section(source: str, language: str, nodes: dict[str, dict[str, Any]], pages: dict[str, list[str]]) -> str:
    source_node = nodes[source]
    cluster = str(source_node["cluster"])
    is_id = language == "id"
    if cluster == "learning":
        kicker = "Jalur belajar terkait" if is_id else "Continue learning"
        heading = "Lanjutkan dari konsep menuju langkah praktis." if is_id else "Move from the concept to the next practical step."
        intro = (
            "Materi berikut dipilih agar Anda dapat memperdalam konsep, mencoba workflow ARSAS, atau menuju troubleshooting yang relevan."
            if is_id else
            "These pages help you deepen the concept, try the matching ARSAS workflow, or move to the relevant troubleshooting path."
        )
    else:
        kicker = "Jalur evidence terkait" if is_id else "Related evidence paths"
        heading = "Lanjutkan ke gejala atau workflow yang paling dekat." if is_id else "Continue with the closest failure mode or workflow."
        intro = (
            "Link berikut dipilih dari cluster engineering yang sama agar diagnosis berpindah dari gejala ke evidence yang relevan."
            if is_id else
            "These links stay inside the same engineering evidence cluster, moving from the current workflow to the most relevant failure mode."
        )
    cards: list[str] = []
    for target in pages[source]:
        target_node = nodes[target]
        target_language = "id" if target in INDONESIAN_PAGES else "en"
        label = localized(target_node, "label", language)
        summary = localized(target_node, "summary", language)
        role = str(target_node["role"])
        role_label = {
            "guide": "Panduan troubleshooting" if is_id else "Troubleshooting guide",
            "capability": "Capability produk" if is_id else "Product capability",
            "solution": "Workflow solusi" if is_id else "Solution workflow",
            "learning": "Materi belajar" if is_id else "Learning article",
            "tutorial": "Tutorial software" if is_id else "Software tutorial",
            "hub": "Pusat belajar" if is_id else "Learning hub",
            "authority": "Referensi engineering" if is_id else "Engineering reference",
        }.get(role, "Jalur terkait" if is_id else "Related path")
        attrs = target_attributes(language, target_language)
        link_label = "Buka materi" if cluster == "learning" and is_id else "Open lesson" if cluster == "learning" else "Buka jalur evidence" if is_id else "Open evidence path"
        cards.append(
            f'<article class="card related-resource-card"><span class="kicker">{escape(role_label)}</span>'
            f'<h3>{escape(label)}</h3><p>{escape(summary)}</p>'
            f'<a class="text-link" href="{escape(target)}"{attrs}>{escape(link_label)} →</a></article>'
        )
    return (
        f'<section class="section section-tight related-resources" data-search-authority="{escape(cluster)}" '
        f'aria-labelledby="related-evidence-heading"><div class="container">'
        f'<div class="section-head"><span class="kicker">{escape(kicker)}</span>'
        f'<h2 id="related-evidence-heading">{escape(heading)}</h2><p>{escape(intro)}</p></div>'
        f'<div class="feature-grid">{"".join(cards)}</div></div></section>\n\n'
    )


def apply(site: Path, graph_path: Path) -> dict[str, int]:
    entries = registry(site)
    graph = load_object(graph_path, "landing/search-authority.json")
    nodes, pages, stats = validate_graph(graph, entries)
    for source in sorted(pages):
        page = site / source
        if not page.is_file():
            raise SystemExit(f"Built page required by authority graph is missing: {source}")
        html = page.read_text(encoding="utf-8")
        if "data-search-authority=" in html:
            raise SystemExit(f"Built page already contains a search-authority section: {source}")
        language = str(entries[source].get("language", "en"))
        generated = section(source, language, nodes, pages)
        marker = '<section class="section section-tight" data-section="download-cta">'
        if marker in html:
            html = html.replace(marker, generated + marker, 1)
        elif "</main>" in html:
            html = html.replace("</main>", generated + "</main>", 1)
        else:
            raise SystemExit(f"Cannot place search-authority section in {source}")
        page.write_text(html, encoding="utf-8")

    build_info_path = site / "build-info.json"
    build_info = load_object(build_info_path, "build-info.json")
    build_info["searchAuthority"] = {
        "schemaVersion": 1,
        **stats,
        "allGuidesHaveCapabilityOrSolutionInbound": stats["guidesWithDiscoveryInbound"] == stats["guideCount"],
    }
    build_info_path.write_text(json.dumps(build_info, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    deployed_graph = site / "search-authority.json"
    if deployed_graph.exists():
        deployed_graph.unlink()
    return stats


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("site", nargs="?", default="_site")
    parser.add_argument("--graph", default=str(DEFAULT_GRAPH))
    args = parser.parse_args()
    stats = apply(Path(args.site).resolve(), Path(args.graph).resolve())
    print(
        "Applied ARSAS search authority graph: "
        f"{stats['mappedPageCount']} pages, {stats['relationshipCount']} contextual links, "
        f"{stats['guidesWithDiscoveryInbound']}/{stats['guideCount']} guides with capability/solution inbound paths."
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ARSAS search authority application failed: {exc}", file=sys.stderr)
        raise
