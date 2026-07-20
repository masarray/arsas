#!/usr/bin/env python3
"""Build a private, aggregated ARSAS website measurement report for GitHub Actions."""

from __future__ import annotations

import argparse
import base64
import json
import os
import sys
import urllib.parse
from collections import defaultdict
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from typing import Any

CANONICAL_ROOT = "https://masarray.github.io/arsas/"
EXPECTED_SITEMAP = CANONICAL_ROOT + "sitemap.xml"
GOOGLE_SCOPES = (
    "https://www.googleapis.com/auth/analytics.readonly",
    "https://www.googleapis.com/auth/webmasters.readonly",
)
DEFAULT_PSI_URLS = (
    CANONICAL_ROOT,
    CANONICAL_ROOT + "download.html",
    CANONICAL_ROOT + "smart-reporting.html",
    CANONICAL_ROOT + "guides.html",
    CANONICAL_ROOT + "id.html",
    CANONICAL_ROOT + "unduh.html",
)


def load_service_account() -> dict[str, Any] | None:
    raw = os.environ.get("GOOGLE_SERVICE_ACCOUNT_JSON", "").strip()
    if not raw:
        return None
    if raw.startswith("base64:"):
        raw = base64.b64decode(raw.removeprefix("base64:")).decode("utf-8")
    value = json.loads(raw)
    if not isinstance(value, dict) or value.get("type") != "service_account":
        raise ValueError("GOOGLE_SERVICE_ACCOUNT_JSON is not a service-account JSON object")
    return value


def authorized_session(service_account_info: dict[str, Any] | None):
    try:
        import google.auth
        from google.auth.transport.requests import AuthorizedSession
        from google.oauth2 import service_account
    except ImportError as exc:
        raise RuntimeError("google-auth is required when Google reporting credentials are configured") from exc

    if service_account_info is not None:
        credentials = service_account.Credentials.from_service_account_info(
            service_account_info,
            scopes=GOOGLE_SCOPES,
        )
        mode = "service-account-secret"
    else:
        credentials, _ = google.auth.default(scopes=GOOGLE_SCOPES)
        mode = "application-default-credentials"
    return AuthorizedSession(credentials), mode


def google_credentials_available(service_account_info: dict[str, Any] | None) -> bool:
    if service_account_info is not None:
        return True
    credential_path = os.environ.get("GOOGLE_APPLICATION_CREDENTIALS", "").strip()
    return bool(credential_path and Path(credential_path).is_file())


def report_rows(payload: dict[str, Any]) -> list[dict[str, Any]]:
    dimensions = [item.get("name", "") for item in payload.get("dimensionHeaders", [])]
    metrics = [item.get("name", "") for item in payload.get("metricHeaders", [])]
    rows: list[dict[str, Any]] = []
    for row in payload.get("rows", []):
        result: dict[str, Any] = {}
        for name, value in zip(dimensions, row.get("dimensionValues", []), strict=False):
            result[name] = value.get("value", "")
        for name, value in zip(metrics, row.get("metricValues", []), strict=False):
            raw = value.get("value", "0")
            try:
                result[name] = float(raw) if "." in raw else int(raw)
            except (TypeError, ValueError):
                result[name] = raw
        rows.append(result)
    return rows


def ga4_report(session, property_id: str, body: dict[str, Any]) -> list[dict[str, Any]]:
    response = session.post(
        f"https://analyticsdata.googleapis.com/v1beta/properties/{property_id}:runReport",
        json=body,
        timeout=45,
    )
    response.raise_for_status()
    return report_rows(response.json())


def read_page_languages(site: Path) -> dict[str, str]:
    config = json.loads((site / "site.json").read_text(encoding="utf-8"))
    result: dict[str, str] = {"index.html": "en"}
    for item in config.get("pages", []):
        path = item.get("path") or "index.html"
        result[str(path)] = str(item.get("language", "en"))
    return result


def normalize_site_path(value: str) -> str:
    parsed = urllib.parse.urlsplit(value)
    path = parsed.path
    prefix = "/arsas/"
    if path == "/arsas" or path == prefix:
        return "index.html"
    if path.startswith(prefix):
        path = path[len(prefix):]
    return path.lstrip("/") or "index.html"


def language_totals(rows: list[dict[str, Any]], page_languages: dict[str, str], metric: str) -> dict[str, float]:
    totals: dict[str, float] = defaultdict(float)
    for row in rows:
        path = normalize_site_path(str(row.get("pagePath", row.get("page", ""))))
        language = page_languages.get(path, "unknown")
        totals[language] += float(row.get(metric, 0) or 0)
    return dict(sorted(totals.items()))


def change(current: float, previous: float) -> dict[str, Any]:
    absolute = current - previous
    return {
        "current": current,
        "previous": previous,
        "absolute": absolute,
        "percent": (absolute / previous) if previous else None,
    }


def ga4_totals(session, property_id: str, start_date: str, end_date: str) -> dict[str, float]:
    rows = ga4_report(session, property_id, {
        "dateRanges": [{"startDate": start_date, "endDate": end_date}],
        "metrics": [{"name": "screenPageViews"}, {"name": "activeUsers"}],
        "limit": "1",
    })
    row = rows[0] if rows else {}
    return {
        "screenPageViews": float(row.get("screenPageViews", 0) or 0),
        "activeUsers": float(row.get("activeUsers", 0) or 0),
    }


def collect_ga4(session, property_id: str, days: int, page_languages: dict[str, str]) -> dict[str, Any]:
    date_range = [{"startDate": f"{days}daysAgo", "endDate": "yesterday"}]
    pages = ga4_report(session, property_id, {
        "dateRanges": date_range,
        "dimensions": [{"name": "pagePath"}, {"name": "pageTitle"}],
        "metrics": [{"name": "screenPageViews"}, {"name": "activeUsers"}],
        "orderBys": [{"metric": {"metricName": "screenPageViews"}, "desc": True}],
        "limit": "10000",
    })
    downloads = ga4_report(session, property_id, {
        "dateRanges": date_range,
        "dimensions": [{"name": "eventName"}, {"name": "pagePath"}],
        "metrics": [{"name": "eventCount"}],
        "dimensionFilter": {
            "filter": {
                "fieldName": "eventName",
                "stringFilter": {
                    "matchType": "FULL_REGEXP",
                    "value": "download_(installer|portable|checksums)",
                },
            }
        },
        "orderBys": [{"metric": {"metricName": "eventCount"}, "desc": True}],
        "limit": "1000",
    })
    not_found = ga4_report(session, property_id, {
        "dateRanges": date_range,
        "dimensions": [{"name": "pagePath"}, {"name": "pageReferrer"}],
        "metrics": [{"name": "eventCount"}],
        "dimensionFilter": {
            "filter": {
                "fieldName": "eventName",
                "stringFilter": {"matchType": "EXACT", "value": "page_not_found"},
            }
        },
        "orderBys": [{"metric": {"metricName": "eventCount"}, "desc": True}],
        "limit": "500",
    })
    current = ga4_totals(session, property_id, f"{days}daysAgo", "yesterday")
    previous = ga4_totals(session, property_id, f"{days * 2}daysAgo", f"{days + 1}daysAgo")
    download_totals: dict[str, int] = defaultdict(int)
    for row in downloads:
        download_totals[str(row.get("eventName", "unknown"))] += int(row.get("eventCount", 0) or 0)
    status = "available" if pages or downloads else "available-no-data"
    return {
        "status": status,
        "topPages": pages[:50],
        "languageTraffic": language_totals(pages, page_languages, "screenPageViews"),
        "downloadEvents": downloads,
        "downloadTotals": dict(sorted(download_totals.items())),
        "notFound": not_found,
        "trend": {
            "pageViews": change(current["screenPageViews"], previous["screenPageViews"]),
            "activeUsers": change(current["activeUsers"], previous["activeUsers"]),
        },
    }


def search_console_query(session, encoded_site: str, body: dict[str, Any]) -> dict[str, Any]:
    response = session.post(
        f"https://searchconsole.googleapis.com/webmasters/v3/sites/{encoded_site}/searchAnalytics/query",
        json=body,
        timeout=60,
    )
    response.raise_for_status()
    payload = response.json()
    return payload if isinstance(payload, dict) else {}


def search_console_rows(
    session,
    encoded_site: str,
    start: date,
    end: date,
    dimensions: list[str],
    max_rows: int = 100000,
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    start_row = 0
    page_size = 25000
    while start_row < max_rows:
        payload = search_console_query(session, encoded_site, {
            "startDate": start.isoformat(),
            "endDate": end.isoformat(),
            "dimensions": dimensions,
            "rowLimit": page_size,
            "startRow": start_row,
            "dataState": "final",
            "type": "web",
        })
        batch = payload.get("rows", [])
        if not isinstance(batch, list):
            break
        rows.extend(item for item in batch if isinstance(item, dict))
        if len(batch) < page_size:
            break
        start_row += page_size
    return rows


def search_console_summary(session, encoded_site: str, start: date, end: date) -> dict[str, float]:
    payload = search_console_query(session, encoded_site, {
        "startDate": start.isoformat(),
        "endDate": end.isoformat(),
        "dataState": "final",
        "type": "web",
    })
    row = payload.get("rows", [{}])
    item = row[0] if isinstance(row, list) and row and isinstance(row[0], dict) else {}
    return {
        "clicks": float(item.get("clicks", 0) or 0),
        "impressions": float(item.get("impressions", 0) or 0),
        "ctr": float(item.get("ctr", 0) or 0),
        "position": float(item.get("position", 0) or 0),
    }


def collect_sitemaps(session, encoded_site: str) -> dict[str, Any]:
    response = session.get(
        f"https://www.googleapis.com/webmasters/v3/sites/{encoded_site}/sitemaps",
        timeout=45,
    )
    response.raise_for_status()
    payload = response.json()
    items = payload.get("sitemap", []) if isinstance(payload, dict) else []
    normalized: list[dict[str, Any]] = []
    expected: dict[str, Any] | None = None
    for item in items if isinstance(items, list) else []:
        if not isinstance(item, dict):
            continue
        value = {
            "path": str(item.get("path", "")),
            "type": str(item.get("type", "")),
            "isPending": bool(item.get("isPending", False)),
            "isSitemapsIndex": bool(item.get("isSitemapsIndex", False)),
            "lastSubmitted": item.get("lastSubmitted"),
            "lastDownloaded": item.get("lastDownloaded"),
            "warnings": int(item.get("warnings", 0) or 0),
            "errors": int(item.get("errors", 0) or 0),
        }
        contents = item.get("contents")
        if isinstance(contents, list):
            value["contents"] = [
                {
                    "type": content.get("type"),
                    "submitted": int(content.get("submitted", 0) or 0),
                    "indexed": int(content.get("indexed", 0) or 0),
                }
                for content in contents
                if isinstance(content, dict)
            ]
        normalized.append(value)
        if value["path"] == EXPECTED_SITEMAP:
            expected = value
    if expected is None:
        status = "missing"
    elif expected["errors"] > 0:
        status = "error"
    elif expected["warnings"] > 0:
        status = "warning"
    elif expected["isPending"]:
        status = "pending"
    else:
        status = "healthy"
    return {
        "status": status,
        "expectedPath": EXPECTED_SITEMAP,
        "expected": expected,
        "entries": normalized,
    }


def collect_search_console(session, site_url: str, days: int, page_languages: dict[str, str]) -> dict[str, Any]:
    end = date.today() - timedelta(days=3)
    start = end - timedelta(days=days - 1)
    previous_end = start - timedelta(days=1)
    previous_start = previous_end - timedelta(days=days - 1)
    encoded_site = urllib.parse.quote(site_url, safe="")

    presence = search_console_rows(session, encoded_site, start, end, ["date"], max_rows=1000)
    rows = search_console_rows(session, encoded_site, start, end, ["query", "page"]) if presence else []
    current = search_console_summary(session, encoded_site, start, end)
    previous = search_console_summary(session, encoded_site, previous_start, previous_end)
    sitemaps = collect_sitemaps(session, encoded_site)

    query_totals: dict[str, dict[str, float]] = defaultdict(lambda: {"clicks": 0, "impressions": 0, "positionWeighted": 0})
    page_totals: dict[str, dict[str, float]] = defaultdict(lambda: {"clicks": 0, "impressions": 0, "positionWeighted": 0})
    detailed: list[dict[str, Any]] = []
    language_impressions: dict[str, float] = defaultdict(float)
    language_clicks: dict[str, float] = defaultdict(float)

    for row in rows:
        keys = row.get("keys", ["", ""])
        query = str(keys[0] if len(keys) > 0 else "")
        page = str(keys[1] if len(keys) > 1 else "")
        clicks = float(row.get("clicks", 0) or 0)
        impressions = float(row.get("impressions", 0) or 0)
        ctr = float(row.get("ctr", 0) or 0)
        position = float(row.get("position", 0) or 0)
        detailed.append({
            "query": query,
            "page": page,
            "clicks": clicks,
            "impressions": impressions,
            "ctr": ctr,
            "position": position,
        })
        for key, bucket in ((query, query_totals), (page, page_totals)):
            bucket[key]["clicks"] += clicks
            bucket[key]["impressions"] += impressions
            bucket[key]["positionWeighted"] += position * impressions
        language = page_languages.get(normalize_site_path(page), "unknown")
        language_impressions[language] += impressions
        language_clicks[language] += clicks

    def finish(values: dict[str, dict[str, float]], label: str) -> list[dict[str, Any]]:
        output: list[dict[str, Any]] = []
        for key, value in values.items():
            impressions = value["impressions"]
            clicks = value["clicks"]
            output.append({
                label: key,
                "clicks": clicks,
                "impressions": impressions,
                "ctr": clicks / impressions if impressions else 0,
                "position": value["positionWeighted"] / impressions if impressions else 0,
            })
        return output

    queries = sorted(finish(query_totals, "query"), key=lambda item: (item["clicks"], item["impressions"]), reverse=True)
    pages = sorted(finish(page_totals, "page"), key=lambda item: (item["clicks"], item["impressions"]), reverse=True)
    query_opportunities = sorted(
        [item for item in queries if item["impressions"] >= 50 and item["ctr"] < 0.03 and item["position"] <= 20],
        key=lambda item: item["impressions"],
        reverse=True,
    )
    page_opportunities = sorted(
        [item for item in pages if item["impressions"] >= 100 and item["ctr"] < 0.03 and item["position"] <= 20],
        key=lambda item: item["impressions"],
        reverse=True,
    )
    status = "available" if presence else "available-no-data"
    return {
        "status": status,
        "siteUrl": site_url,
        "dateRange": {"start": start.isoformat(), "end": end.isoformat()},
        "previousDateRange": {"start": previous_start.isoformat(), "end": previous_end.isoformat()},
        "dataPresenceDays": len(presence),
        "topQueries": queries[:50],
        "topPages": pages[:50],
        "lowCtrQueries": query_opportunities[:50],
        "lowCtrPages": page_opportunities[:50],
        "languageImpressions": dict(sorted(language_impressions.items())),
        "languageClicks": dict(sorted(language_clicks.items())),
        "rowCount": len(detailed),
        "trend": {
            "clicks": change(current["clicks"], previous["clicks"]),
            "impressions": change(current["impressions"], previous["impressions"]),
            "ctr": change(current["ctr"], previous["ctr"]),
            "position": change(current["position"], previous["position"]),
        },
        "sitemaps": sitemaps,
    }


def metric_value(metrics: dict[str, Any], names: tuple[str, ...], scale: float = 1.0) -> dict[str, Any] | None:
    for name in names:
        item = metrics.get(name)
        if isinstance(item, dict) and item.get("percentile") is not None:
            return {
                "value": float(item["percentile"]) / scale,
                "category": str(item.get("category", "UNKNOWN")).lower(),
            }
    return None


def collect_pagespeed(urls: tuple[str, ...], api_key: str) -> dict[str, Any]:
    import requests

    results: list[dict[str, Any]] = []
    for url in urls:
        params = [("url", url), ("strategy", "mobile"), ("category", "performance")]
        if api_key:
            params.append(("key", api_key))
        try:
            response = requests.get(
                "https://www.googleapis.com/pagespeedonline/v5/runPagespeed",
                params=params,
                timeout=90,
                headers={"User-Agent": "ARSAS-Measurement/2.0"},
            )
            response.raise_for_status()
            payload = response.json()
            field = payload.get("loadingExperience", {})
            metrics = field.get("metrics", {}) if isinstance(field, dict) else {}
            audits = payload.get("lighthouseResult", {}).get("audits", {})
            results.append({
                "url": url,
                "status": "available",
                "fieldCategory": str(field.get("overall_category", "NONE")).lower(),
                "field": {
                    "lcpMs": metric_value(metrics, ("LARGEST_CONTENTFUL_PAINT_MS",)),
                    "cls": metric_value(metrics, ("CUMULATIVE_LAYOUT_SHIFT_SCORE",), 100.0),
                    "inpMs": metric_value(metrics, ("INTERACTION_TO_NEXT_PAINT", "INTERACTION_TO_NEXT_PAINT_MS")),
                },
                "lab": {
                    "performanceScore": payload.get("lighthouseResult", {}).get("categories", {}).get("performance", {}).get("score"),
                    "lcpMs": audits.get("largest-contentful-paint", {}).get("numericValue"),
                    "cls": audits.get("cumulative-layout-shift", {}).get("numericValue"),
                    "totalBlockingTimeMs": audits.get("total-blocking-time", {}).get("numericValue"),
                },
            })
        except Exception as exc:
            results.append({"url": url, "status": "unavailable", "error": str(exc)})
    return {"status": "available" if any(item["status"] == "available" for item in results) else "unavailable", "pages": results}


def percent(value: float) -> str:
    return f"{value * 100:.2f}%"


def trend_text(value: dict[str, Any], percent_value: bool = False) -> str:
    current = float(value.get("current", 0) or 0)
    previous = float(value.get("previous", 0) or 0)
    delta = value.get("percent")
    current_text = percent(current) if percent_value else f"{current:.0f}"
    previous_text = percent(previous) if percent_value else f"{previous:.0f}"
    delta_text = "new baseline" if delta is None else f"{float(delta) * 100:+.1f}%"
    return f"{current_text} vs {previous_text} ({delta_text})"


def table(headers: tuple[str, ...], rows: list[tuple[Any, ...]]) -> list[str]:
    if not rows:
        return ["No data available.", ""]
    lines = ["| " + " | ".join(headers) + " |", "|" + "|".join("---" for _ in headers) + "|"]
    for row in rows:
        lines.append("| " + " | ".join(str(value).replace("|", "\\|") for value in row) + " |")
    lines.append("")
    return lines


def build_markdown(report: dict[str, Any]) -> str:
    ga4 = report["ga4"]
    search = report["searchConsole"]
    speed = report["coreWebVitals"]
    lines = [
        "# ARSAS website measurement",
        "",
        f"Generated: `{report['generatedAtUtc']}` · Window: **{report['days']} days**",
        "",
        "## Data coverage",
        "",
        f"- Google authentication: **{report.get('authenticationMode', 'not-configured')}**",
        f"- GA4 aggregated traffic/events: **{ga4['status']}**",
        f"- Search Console queries/impressions/CTR: **{search['status']}**",
        f"- Search Console sitemap: **{search.get('sitemaps', {}).get('status', 'not-configured')}**",
        f"- CrUX/PageSpeed Core Web Vitals: **{speed['status']}**",
        "- Broken links and deployed 404 behavior: see the paired `site-health.md` artifact.",
        "",
    ]

    lines.extend(["## Period-over-period trend", ""])
    trend_rows: list[tuple[Any, ...]] = []
    if isinstance(ga4.get("trend"), dict):
        trend_rows.extend([
            ("GA4 page views", trend_text(ga4["trend"].get("pageViews", {}))),
            ("GA4 active users", trend_text(ga4["trend"].get("activeUsers", {}))),
        ])
    if isinstance(search.get("trend"), dict):
        trend_rows.extend([
            ("Search clicks", trend_text(search["trend"].get("clicks", {}))),
            ("Search impressions", trend_text(search["trend"].get("impressions", {}))),
            ("Search CTR", trend_text(search["trend"].get("ctr", {}), True)),
        ])
    lines.extend(table(("Metric", "Current vs previous window"), trend_rows))

    lines.extend(["## Sitemap processing", ""])
    sitemap = search.get("sitemaps", {})
    expected = sitemap.get("expected") if isinstance(sitemap, dict) else None
    sitemap_rows = []
    if isinstance(expected, dict):
        sitemap_rows.append((
            expected.get("path", ""), expected.get("lastDownloaded", "—"),
            expected.get("warnings", 0), expected.get("errors", 0), expected.get("isPending", False),
        ))
    lines.extend(table(("Sitemap", "Last downloaded", "Warnings", "Errors", "Pending"), sitemap_rows))

    lines.extend(["## Most visited pages", ""])
    lines.extend(table(
        ("Page", "Views", "Active users"),
        [(item.get("pagePath", ""), item.get("screenPageViews", 0), item.get("activeUsers", 0)) for item in ga4.get("topPages", [])[:15]],
    ))

    lines.extend(["## English vs Indonesian traffic", ""])
    language_rows = []
    for language in sorted(set(ga4.get("languageTraffic", {})) | set(search.get("languageImpressions", {}))):
        language_rows.append((
            language,
            int(ga4.get("languageTraffic", {}).get(language, 0)),
            int(search.get("languageImpressions", {}).get(language, 0)),
            int(search.get("languageClicks", {}).get(language, 0)),
        ))
    lines.extend(table(("Language", "Page views", "Search impressions", "Search clicks"), language_rows))

    lines.extend(["## Download button clicks", ""])
    lines.extend(table(
        ("Package event", "Clicks"),
        [(name, count) for name, count in ga4.get("downloadTotals", {}).items()],
    ))

    lines.extend(["## Search queries bringing users", ""])
    lines.extend(table(
        ("Query", "Clicks", "Impressions", "CTR", "Avg position"),
        [(item["query"], int(item["clicks"]), int(item["impressions"]), percent(item["ctr"]), f"{item['position']:.1f}") for item in search.get("topQueries", [])[:20]],
    ))

    lines.extend(["## High-impression pages with low click-through", ""])
    lines.extend(table(
        ("Page", "Clicks", "Impressions", "CTR", "Avg position"),
        [(item["page"], int(item["clicks"]), int(item["impressions"]), percent(item["ctr"]), f"{item['position']:.1f}") for item in search.get("lowCtrPages", [])[:20]],
    ))

    lines.extend(["## High-impression queries with low click-through", ""])
    lines.extend(table(
        ("Query", "Clicks", "Impressions", "CTR", "Avg position"),
        [(item["query"], int(item["clicks"]), int(item["impressions"]), percent(item["ctr"]), f"{item['position']:.1f}") for item in search.get("lowCtrQueries", [])[:20]],
    ))

    lines.extend(["## 404 observations", ""])
    lines.extend(table(
        ("Requested path", "Referrer", "Events"),
        [(item.get("pagePath", ""), item.get("pageReferrer", ""), item.get("eventCount", 0)) for item in ga4.get("notFound", [])[:20]],
    ))

    lines.extend(["## Core Web Vitals", ""])
    vital_rows = []
    for item in speed.get("pages", []):
        field = item.get("field", {})
        vital_rows.append((
            item.get("url", ""),
            item.get("fieldCategory", item.get("status", "")),
            (field.get("lcpMs") or {}).get("value", "—"),
            (field.get("cls") or {}).get("value", "—"),
            (field.get("inpMs") or {}).get("value", "—"),
            item.get("lab", {}).get("performanceScore", "—"),
        ))
    lines.extend(table(("URL", "Field status", "LCP ms", "CLS", "INP ms", "Lab score"), vital_rows))

    lines.extend(["## Continuous-improvement queue", ""])
    actions: list[str] = []
    sitemap_status = search.get("sitemaps", {}).get("status") if isinstance(search.get("sitemaps"), dict) else None
    if sitemap_status in {"missing", "error", "warning"}:
        actions.append(f"Resolve Search Console sitemap state `{sitemap_status}` before interpreting indexing trends.")
    for item in search.get("lowCtrPages", [])[:5]:
        actions.append(f"Rewrite title/meta and align intent for `{item['page']}` ({int(item['impressions'])} impressions, {percent(item['ctr'])} CTR).")
    for item in speed.get("pages", []):
        if item.get("fieldCategory") in {"slow", "poor"}:
            actions.append(f"Prioritize Core Web Vitals remediation for `{item['url']}`.")
    if ga4.get("notFound"):
        actions.append("Add a precise route or repair the source link for the highest-frequency 404 paths.")
    if ga4.get("status") not in {"available", "available-no-data"}:
        actions.append("Configure GA4 property access to activate traffic and download reporting.")
    if search.get("status") not in {"available", "available-no-data"}:
        actions.append("Grant read-only access to the exact Search Console URL-prefix property.")
    if ga4.get("status") == "available-no-data":
        actions.append("GA4 access works but no consented data exists in this window; do not infer zero demand.")
    if search.get("status") == "available-no-data":
        actions.append("Search Console access works but finalized search data is not yet present; continue the weekly cycle.")
    if not actions:
        actions.append("No immediate threshold breach was detected; continue the weekly measurement cycle.")
    lines.extend(f"- {item}" for item in actions)
    lines.append("")
    lines.append("Reports contain aggregated product-site metrics only and are kept in GitHub Actions artifacts rather than deployed publicly.")
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--site", default="_site")
    parser.add_argument("--output", default="_measurement")
    parser.add_argument("--days", type=int, default=28)
    args = parser.parse_args()
    if not 7 <= args.days <= 90:
        raise SystemExit("Measurement window must be between 7 and 90 days")

    site = Path(args.site).resolve()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    page_languages = read_page_languages(site)
    report: dict[str, Any] = {
        "schemaVersion": 2,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "days": args.days,
        "authenticationMode": "not-configured",
        "ga4": {"status": "not-configured", "topPages": [], "languageTraffic": {}, "downloadTotals": {}, "notFound": []},
        "searchConsole": {"status": "not-configured", "topQueries": [], "topPages": [], "lowCtrQueries": [], "lowCtrPages": [], "languageImpressions": {}, "languageClicks": {}, "sitemaps": {"status": "not-configured"}},
        "coreWebVitals": {"status": "not-run", "pages": []},
    }

    service_info = None
    try:
        service_info = load_service_account()
    except Exception as exc:
        report["credentialError"] = str(exc)

    property_id = os.environ.get("GA4_PROPERTY_ID", "").strip()
    search_site = os.environ.get("GSC_SITE_URL", CANONICAL_ROOT).strip()
    if google_credentials_available(service_info):
        try:
            session, auth_mode = authorized_session(service_info)
            report["authenticationMode"] = auth_mode
            if property_id.isdigit():
                try:
                    report["ga4"] = collect_ga4(session, property_id, args.days, page_languages)
                except Exception as exc:
                    report["ga4"] = {**report["ga4"], "status": "unavailable", "error": str(exc)}
            elif property_id:
                report["ga4"] = {**report["ga4"], "status": "invalid-property-id"}
            try:
                report["searchConsole"] = collect_search_console(session, search_site, args.days, page_languages)
            except Exception as exc:
                report["searchConsole"] = {**report["searchConsole"], "status": "unavailable", "error": str(exc)}
        except Exception as exc:
            report["googleSessionError"] = str(exc)

    psi_urls = tuple(
        item.strip() for item in os.environ.get("PAGESPEED_URLS", ",".join(DEFAULT_PSI_URLS)).split(",") if item.strip()
    )
    try:
        report["coreWebVitals"] = collect_pagespeed(psi_urls, os.environ.get("PAGESPEED_API_KEY", "").strip())
    except Exception as exc:
        report["coreWebVitals"] = {"status": "unavailable", "pages": [], "error": str(exc)}

    markdown = build_markdown(report)
    (output / "measurement.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    (output / "measurement.md").write_text(markdown, encoding="utf-8")

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(markdown)
    print(
        "ARSAS measurement report generated: "
        f"auth={report['authenticationMode']}, GA4={report['ga4']['status']}, "
        f"Search Console={report['searchConsole']['status']}, sitemap={report['searchConsole'].get('sitemaps', {}).get('status')}, "
        f"Core Web Vitals={report['coreWebVitals']['status']}."
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ARSAS measurement report failed: {exc}", file=sys.stderr)
        raise
