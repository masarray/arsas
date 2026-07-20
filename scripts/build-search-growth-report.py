#!/usr/bin/env python3
"""Build a private ARSAS search-growth queue from structure and optional measurement evidence."""

from __future__ import annotations

import argparse
import json
import math
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_GRAPH = ROOT / "landing" / "search-authority.json"
CANONICAL_PREFIX = "/arsas/"


def load(path: Path, required: bool = True) -> dict[str, Any]:
    if not path.exists():
        if required:
            raise SystemExit(f"Missing JSON input: {path}")
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise SystemExit(f"Invalid JSON input {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise SystemExit(f"JSON input must contain an object: {path}")
    return value


def normalize_path(value: str) -> str:
    parsed = urlsplit(value)
    path = parsed.path
    if path in {"/arsas", "/arsas/", ""}:
        return "index.html"
    if path.startswith(CANONICAL_PREFIX):
        path = path[len(CANONICAL_PREFIX):]
    return path.lstrip("/") or "index.html"


def registry(site: Path) -> dict[str, dict[str, Any]]:
    config = load(site / "site.json")
    result: dict[str, dict[str, Any]] = {}
    for item in config.get("pages", []):
        if isinstance(item, dict) and isinstance(item.get("path"), str):
            result[item["path"] or "index.html"] = item
    return result


def localized_counterparts(entries: dict[str, dict[str, Any]]) -> dict[str, str]:
    pairs: dict[str, str] = {}
    for path, item in entries.items():
        if item.get("language", "en") != "en":
            continue
        alternates = item.get("alternates")
        if isinstance(alternates, dict) and isinstance(alternates.get("id"), str):
            pairs[path] = alternates["id"] or "id.html"
    return pairs


def add(queue: list[dict[str, Any]], *, priority: int, category: str, page: str = "", query: str = "", evidence: str, action: str, source: str) -> None:
    queue.append({
        "priority": max(1, min(100, int(priority))),
        "category": category,
        "page": page,
        "query": query,
        "evidence": evidence,
        "action": action,
        "source": source,
    })


def structural_queue(entries: dict[str, dict[str, Any]], graph: dict[str, Any]) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    nodes = graph.get("nodes", {})
    pages = graph.get("pages", {})
    inbound: dict[str, int] = defaultdict(int)
    for targets in pages.values():
        if isinstance(targets, list):
            for target in targets:
                if isinstance(target, str):
                    inbound[target] += 1

    queue: list[dict[str, Any]] = []
    pairs = localized_counterparts(entries)
    localized_targets = set(pairs.values())
    for path, node in nodes.items():
        if not isinstance(node, dict):
            continue
        role = str(node.get("role", ""))
        language = str(entries.get(path, {}).get("language", "en"))
        if language == "en" and role in {"guide", "capability", "solution"} and path not in pairs:
            base = {"guide": 68, "capability": 54, "solution": 50}.get(role, 45)
            add(
                queue,
                priority=base,
                category="localization-gap",
                page=path,
                query=str(node.get("primaryQuery", "")),
                evidence=f"Indexable English {role} page has no declared Indonesian alternate.",
                action="Translate only after search impressions or Indonesian demand justify the work; then add reciprocal hreflang and hub links.",
                source="authority-graph",
            )
        if inbound[path] < 2:
            add(
                queue,
                priority=42 if role == "guide" else 32,
                category="authority-depth",
                page=path,
                query=str(node.get("primaryQuery", "")),
                evidence=f"Contextual authority graph provides {inbound[path]} inbound relationship(s).",
                action="Add another relevant contextual path from a capability, solution or sibling guide without creating generic sitewide links.",
                source="authority-graph",
            )

    stats = {
        "indexablePages": sum(1 for item in entries.values() if item.get("index", True) is not False),
        "authorityNodes": len(nodes),
        "mappedPages": len(pages),
        "relationships": sum(len(value) for value in pages.values() if isinstance(value, list)),
        "localizedPairs": len(pairs),
        "localizedTargets": len(localized_targets),
        "structuralQueueItems": len(queue),
    }
    return queue, stats


def measurement_queue(report: dict[str, Any]) -> tuple[list[dict[str, Any]], dict[str, str]]:
    queue: list[dict[str, Any]] = []
    ga4 = report.get("ga4") if isinstance(report.get("ga4"), dict) else {}
    search = report.get("searchConsole") if isinstance(report.get("searchConsole"), dict) else {}
    speed = report.get("coreWebVitals") if isinstance(report.get("coreWebVitals"), dict) else {}

    for item in search.get("lowCtrPages", [])[:50]:
        if not isinstance(item, dict):
            continue
        impressions = float(item.get("impressions", 0) or 0)
        ctr = float(item.get("ctr", 0) or 0)
        position = float(item.get("position", 0) or 0)
        score = 62 + min(25, math.log10(max(impressions, 1)) * 7) + (8 if position <= 10 else 0)
        add(
            queue,
            priority=score,
            category="low-ctr-page",
            page=normalize_path(str(item.get("page", ""))),
            evidence=f"{int(impressions)} impressions, {ctr * 100:.2f}% CTR, average position {position:.1f}.",
            action="Review title, description, visible answer and query intent before changing page scope; compare the next reporting window.",
            source="search-console",
        )

    for item in search.get("lowCtrQueries", [])[:50]:
        if not isinstance(item, dict):
            continue
        impressions = float(item.get("impressions", 0) or 0)
        ctr = float(item.get("ctr", 0) or 0)
        position = float(item.get("position", 0) or 0)
        score = 58 + min(22, math.log10(max(impressions, 1)) * 6) + (8 if position <= 10 else 0)
        add(
            queue,
            priority=score,
            category="low-ctr-query",
            query=str(item.get("query", "")),
            evidence=f"{int(impressions)} impressions, {ctr * 100:.2f}% CTR, average position {position:.1f}.",
            action="Map the query to one existing evidence page first; improve intent alignment rather than creating a near-duplicate page.",
            source="search-console",
        )

    for item in ga4.get("notFound", [])[:30]:
        if not isinstance(item, dict):
            continue
        count = int(item.get("eventCount", 0) or 0)
        add(
            queue,
            priority=92 if count >= 5 else 78,
            category="observed-404",
            page=normalize_path(str(item.get("pagePath", ""))),
            evidence=f"{count} consented 404 event(s); referrer {item.get('pageReferrer', '') or 'unavailable'}.",
            action="Repair the source link or add a precise valid route; do not redirect unrelated paths to the homepage.",
            source="ga4",
        )

    for item in speed.get("pages", []):
        if not isinstance(item, dict) or item.get("status") != "available":
            continue
        category = str(item.get("fieldCategory", "none")).lower()
        lab_score = item.get("lab", {}).get("performanceScore") if isinstance(item.get("lab"), dict) else None
        poor_lab = isinstance(lab_score, (int, float)) and lab_score < 0.75
        if category in {"slow", "poor"} or poor_lab:
            add(
                queue,
                priority=86 if category in {"slow", "poor"} else 70,
                category="core-web-vitals",
                page=normalize_path(str(item.get("url", ""))),
                evidence=f"Field category {category}; Lighthouse performance score {lab_score if lab_score is not None else 'unavailable'}.",
                action="Profile the exact page and remove the largest blocking or layout-shift source; confirm with the next field-data cycle.",
                source="pagespeed-crux",
            )

    coverage = {
        "ga4": str(ga4.get("status", "not-configured")),
        "searchConsole": str(search.get("status", "not-configured")),
        "coreWebVitals": str(speed.get("status", "not-run")),
    }
    return queue, coverage


def deduplicate(queue: list[dict[str, Any]]) -> list[dict[str, Any]]:
    best: dict[tuple[str, str, str], dict[str, Any]] = {}
    for item in queue:
        key = (str(item.get("category", "")), str(item.get("page", "")), str(item.get("query", "")))
        if key not in best or int(item["priority"]) > int(best[key]["priority"]):
            best[key] = item
    return sorted(best.values(), key=lambda item: (-int(item["priority"]), str(item["category"]), str(item["page"]), str(item["query"])))


def markdown(payload: dict[str, Any]) -> str:
    coverage = payload["dataCoverage"]
    lines = [
        "# ARSAS search growth queue",
        "",
        f"Generated: `{payload['generatedAtUtc']}`",
        "",
        "## Evidence coverage",
        "",
        f"- GA4: **{coverage['ga4']}**",
        f"- Search Console: **{coverage['searchConsole']}**",
        f"- Core Web Vitals: **{coverage['coreWebVitals']}**",
        f"- Authority graph: **available** ({payload['structure']['mappedPages']} mapped pages, {payload['structure']['relationships']} relationships)",
        "",
        "## Prioritized actions",
        "",
        "| Priority | Category | Page / query | Evidence | Action |",
        "|---:|---|---|---|---|",
    ]
    for item in payload["queue"][:50]:
        subject = item.get("page") or item.get("query") or "—"
        values = [
            str(item["priority"]), str(item["category"]), str(subject),
            str(item["evidence"]), str(item["action"]),
        ]
        lines.append("| " + " | ".join(value.replace("|", "\\|").replace("\n", " ") for value in values) + " |")
    if not payload["queue"]:
        lines.append("| — | none | — | No threshold breach or structural gap detected. | Continue the weekly evidence cycle. |")
    lines.extend([
        "",
        "This queue is private workflow evidence. It does not publish traffic, queries, credentials or customer/project data to the website.",
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--site", default="_site")
    parser.add_argument("--graph", default=str(DEFAULT_GRAPH))
    parser.add_argument("--measurement-json", default="")
    parser.add_argument("--output", default="_growth")
    args = parser.parse_args()

    site = Path(args.site).resolve()
    graph = load(Path(args.graph).resolve())
    entries = registry(site)
    structural, structure_stats = structural_queue(entries, graph)
    measurement = load(Path(args.measurement_json).resolve(), required=False) if args.measurement_json else {}
    measured, coverage = measurement_queue(measurement)
    queue = deduplicate([*measured, *structural])
    payload = {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "dataCoverage": coverage,
        "structure": structure_stats,
        "queue": queue,
    }
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    (output / "search-growth.json").write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    (output / "search-growth.md").write_text(markdown(payload), encoding="utf-8")
    print(f"ARSAS search growth queue generated: {len(queue)} prioritized item(s), output {output}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
