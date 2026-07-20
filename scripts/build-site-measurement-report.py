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


def authorized_session(service_account_info: dict[str, Any]):
    try:
        from google.auth.transport.requests import AuthorizedSession
        from google.oauth2 import service_account
    except ImportError as exc:
        raise RuntimeError("google-auth is required when Google reporting credentials are configured") from exc

    credentials = service_account.Credentials.from_service_account_info(
        service_account_info,
        scopes=(
            "https://www.googleapis.com/auth/analytics.readonly",
            "https://www.googleapis.com/auth/webmasters.readonly",
        ),
    )
    return AuthorizedSession(credentials)


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
    download_totals: dict[str, int] = defaultdict(int)
    for row in downloads:
        download_totals[str(row.get("eventName", "unknown"))] += int(row.get("eventCount", 0) or 0)
    return {
        "status": "available",
        "topPages": pages[:50],
        "languageTraffic": language_totals(pages, page_languages, "screenPageViews"),
        "downloadEvents": downloads,
        "downloadTotals": dict(sorted(download_totals.items())),
        "notFound": not_found,
    }


def collect_search_console(session, site_url: str, days: int, page_languages: dict[str, str]) -> dict[str, Any]:
    end = date.today() - timedelta(days=3)
    start = end - timedelta(days=days - 1)
    encoded_site = urllib.parse.quote(site_url, safe="")
    response = session.post(
        f"https://searchconsole.googleapis.com/webmasters/v3/sites/{encoded_site}/searchAnalytics/query",
        json={
            "startDate": start.isoformat(),
            "endDate": end.isoformat(),
            "dimensions": ["query", "page"],
            "rowLimit": 25000,
            "dataState": "final",
        },
        timeout=60,
    )
    response.raise_for_status()
    rows = response.json().get("rows", [])

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
    return {
        "status": "available",
        "siteUrl": site_url,
        "dateRange": {"start": start.isoformat(), "end": end.isoformat()},
        "topQueries": queries[:50],
        "topPages": pages[:50],
        "lowCtrQueries": query_opportunities[:50],
        "lowCtrPages": page_opportunities[:50],
        "languageImpressions": dict(sorted(language_impressions.items())),
        "languageClicks": dict(sorted(language_clicks.items())),
        "rowCount": len(detailed),
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
                headers={"User-Agent": "ARSAS-Measurement/1.0"},
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
        except Exception as exc:  # network/API errors should not erase other measurement sources
            results.append({"url": url, "status": "unavailable", "error": str(exc)})
    return {"status": "available" if any(item["status"] == "available" for item in results) else "unavailable", "pages": results}


def percent(value: float) -> str:
    return f"{value * 100:.2f}%"


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
        f"- GA4 aggregated traffic/events: **{ga4['status']}**",
        f"- Search Console queries/impressions/CTR: **{search['status']}**",
        f"- CrUX/PageSpeed Core Web Vitals: **{speed['status']}**",
        "- Broken links and deployed 404 behavior: see the paired `site-health.md` artifact.",
        "",
    ]

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
    for item in search.get("lowCtrPages", [])[:5]:
        actions.append(f"Rewrite title/meta and align intent for `{item['page']}` ({int(item['impressions'])} impressions, {percent(item['ctr'])} CTR).")
    for item in speed.get("pages", []):
        if item.get("fieldCategory") in {"slow", "poor"}:
            actions.append(f"Prioritize Core Web Vitals remediation for `{item['url']}`.")
    if ga4.get("notFound"):
        actions.append("Add redirects or repair inbound links for the highest-frequency 404 paths.")
    if ga4.get("status") != "available":
        actions.append("Configure GA4 repository variables and read-only service-account access to activate traffic and download reporting.")
    if search.get("status") != "available":
        actions.append("Grant the service account read access to the verified Search Console property to activate query and CTR reporting.")
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
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "days": args.days,
        "ga4": {"status": "not-configured", "topPages": [], "languageTraffic": {}, "downloadTotals": {}, "notFound": []},
        "searchConsole": {"status": "not-configured", "topQueries": [], "topPages": [], "lowCtrQueries": [], "lowCtrPages": [], "languageImpressions": {}, "languageClicks": {}},
        "coreWebVitals": {"status": "not-run", "pages": []},
    }

    service_info = None
    try:
        service_info = load_service_account()
    except Exception as exc:
        report["credentialError"] = str(exc)

    property_id = os.environ.get("GA4_PROPERTY_ID", "").strip()
    search_site = os.environ.get("GSC_SITE_URL", CANONICAL_ROOT).strip()
    if service_info:
        try:
            session = authorized_session(service_info)
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
        f"GA4={report['ga4']['status']}, Search Console={report['searchConsole']['status']}, "
        f"Core Web Vitals={report['coreWebVitals']['status']}."
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ARSAS measurement report failed: {exc}", file=sys.stderr)
        raise
