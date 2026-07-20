#!/usr/bin/env python3
"""Validate rendered ARSAS search-authority relationships and build metadata."""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_GRAPH = ROOT / "landing" / "search-authority.json"
SECTION_PATTERN = re.compile(
    r'<section\s+class="[^"]*related-resources[^"]*"\s+data-search-authority="([^"]+)"[^>]*>(.*?)</section>',
    re.IGNORECASE | re.DOTALL,
)
HREF_PATTERN = re.compile(r'href="([^"]+)"', re.IGNORECASE)


def load(path: Path, label: str) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise SystemExit(f"Cannot read {label}: {exc}") from exc
    if not isinstance(value, dict):
        raise SystemExit(f"{label} must contain an object")
    return value


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("site", nargs="?", default="_site")
    parser.add_argument("--graph", default=str(DEFAULT_GRAPH))
    args = parser.parse_args()

    site = Path(args.site).resolve()
    graph = load(Path(args.graph).resolve(), "search authority graph")
    config = load(site / "site.json", "built site registry")
    build_info = load(site / "build-info.json", "build-info.json")
    errors: list[str] = []

    nodes = graph.get("nodes")
    pages = graph.get("pages")
    clusters = graph.get("clusters")
    if not isinstance(nodes, dict) or not isinstance(pages, dict) or not isinstance(clusters, dict):
        raise SystemExit("Search authority graph is incomplete")

    registry: dict[str, dict[str, Any]] = {}
    for item in config.get("pages", []):
        if isinstance(item, dict) and isinstance(item.get("path"), str):
            registry[item["path"] or "index.html"] = item

    inbound: dict[str, int] = defaultdict(int)
    relationship_count = 0
    for source, raw_targets in pages.items():
        targets = raw_targets if isinstance(raw_targets, list) else []
        relationship_count += len(targets)
        page = site / source
        if not page.is_file():
            errors.append(f"mapped page is missing: {source}")
            continue
        html = page.read_text(encoding="utf-8")
        matches = SECTION_PATTERN.findall(html)
        if len(matches) != 1:
            errors.append(f"{source}: expected one rendered authority section, found {len(matches)}")
            continue
        cluster, body = matches[0]
        node = nodes.get(source)
        expected_cluster = node.get("cluster") if isinstance(node, dict) else None
        if cluster != expected_cluster:
            errors.append(f"{source}: authority cluster is {cluster!r}, expected {expected_cluster!r}")
        hrefs = HREF_PATTERN.findall(body)
        for target in targets:
            if hrefs.count(target) != 1:
                errors.append(f"{source}: expected exactly one contextual link to {target}")
            inbound[target] += 1
        unexpected = sorted(set(hrefs) - set(targets))
        if unexpected:
            errors.append(f"{source}: authority section has unexpected links {unexpected}")
        if html.find('data-search-authority=') > html.find('data-section="download-cta"') >= 0:
            errors.append(f"{source}: authority section must appear before the download CTA")
        if 'aria-labelledby="related-evidence-heading"' not in html:
            errors.append(f"{source}: authority section is missing accessible heading linkage")

    for path in registry:
        if path in pages:
            continue
        page = site / path
        if page.is_file() and "data-search-authority=" in page.read_text(encoding="utf-8"):
            errors.append(f"{path}: unexpected authority section on unmapped page")

    guide_paths = {
        path for path, item in registry.items()
        if item.get("contentType") == "guide"
    }
    for guide in sorted(guide_paths):
        if inbound[guide] < 1:
            errors.append(f"{guide}: no rendered inbound authority relationship")

    authority = build_info.get("searchAuthority")
    expected_stats = {
        "schemaVersion": 1,
        "clusterCount": len(clusters),
        "nodeCount": len(nodes),
        "mappedPageCount": len(pages),
        "guideCount": len(guide_paths),
        "guidesWithDiscoveryInbound": len(guide_paths),
        "relationshipCount": relationship_count,
        "allGuidesHaveCapabilityOrSolutionInbound": True,
    }
    if not isinstance(authority, dict):
        errors.append("build-info.json is missing searchAuthority metadata")
    else:
        for key, value in expected_stats.items():
            if authority.get(key) != value:
                errors.append(f"build-info.json searchAuthority.{key} must be {value!r}")

    if (site / "search-authority.json").exists():
        errors.append("source search-authority.json must not be deployed publicly")

    errors = list(dict.fromkeys(errors))
    if errors:
        print("ARSAS search authority validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print(
        f"ARSAS search authority validation passed: {len(pages)} mapped pages, "
        f"{relationship_count} contextual links, {len(guide_paths)} guides with inbound paths."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
