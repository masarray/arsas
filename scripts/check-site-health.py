#!/usr/bin/env python3
"""Check generated ARSAS links locally and optionally verify the deployed site."""

from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import asdict, dataclass
from html.parser import HTMLParser
from pathlib import Path

CANONICAL_ROOT = "https://masarray.github.io/arsas/"
USER_AGENT = "ARSAS-Site-Health/1.0"
SKIP_SCHEMES = {"mailto", "tel", "javascript", "data"}


class PageParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.refs: list[tuple[str, str]] = []
        self.ids: set[str] = set()

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        element_id = values.get("id")
        if element_id:
            self.ids.add(element_id)
        for key in ("href", "src"):
            value = values.get(key)
            if value:
                self.refs.append((key, value))


@dataclass
class Finding:
    severity: str
    source: str
    target: str
    message: str


def parse_pages(site: Path) -> dict[Path, PageParser]:
    parsed: dict[Path, PageParser] = {}
    for page in sorted(site.rglob("*.html")):
        parser = PageParser()
        parser.feed(page.read_text(encoding="utf-8"))
        parsed[page.resolve()] = parser
    return parsed


def local_target(site: Path, page: Path, reference: str) -> tuple[Path | None, str]:
    split = urllib.parse.urlsplit(reference)
    if split.scheme in SKIP_SCHEMES:
        return None, ""
    if split.scheme in {"http", "https"}:
        if reference.startswith(CANONICAL_ROOT):
            relative = urllib.parse.urlsplit(reference[len(CANONICAL_ROOT):]).path
            target = site / (relative or "index.html")
            return target.resolve(), split.fragment
        return None, ""
    if split.netloc:
        return None, ""
    clean = urllib.parse.unquote(split.path)
    if not clean:
        target = page
    elif clean.endswith("/"):
        target = page.parent / clean / "index.html"
    else:
        target = page.parent / clean
    return target.resolve(), split.fragment


def check_local(site: Path) -> list[Finding]:
    findings: list[Finding] = []
    pages = parse_pages(site)
    for page, parser in pages.items():
        source = str(page.relative_to(site))
        for _, reference in parser.refs:
            target, fragment = local_target(site, page, reference)
            if target is None:
                continue
            try:
                target.relative_to(site)
            except ValueError:
                findings.append(Finding("error", source, reference, "local reference escapes the site root"))
                continue
            if not target.exists():
                findings.append(Finding("error", source, reference, "target does not exist"))
                continue
            if fragment and target.suffix.lower() == ".html":
                target_parser = pages.get(target)
                if target_parser is None:
                    target_parser = PageParser()
                    target_parser.feed(target.read_text(encoding="utf-8"))
                    pages[target] = target_parser
                if fragment not in target_parser.ids:
                    findings.append(Finding("error", source, reference, f"fragment #{fragment} does not exist"))
    return findings


def request_status(url: str, timeout: float = 20.0) -> tuple[int, str]:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "text/html,*/*"})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return int(response.status), response.geturl()
    except urllib.error.HTTPError as exc:
        return int(exc.code), exc.geturl()
    except (urllib.error.URLError, TimeoutError) as exc:
        return 0, str(exc)


def sitemap_urls(site: Path) -> list[str]:
    import xml.etree.ElementTree as ET

    tree = ET.parse(site / "sitemap.xml")
    namespace = {"sm": "http://www.sitemaps.org/schemas/sitemap/0.9"}
    return [
        (node.text or "").strip()
        for node in tree.findall("sm:url/sm:loc", namespace)
        if (node.text or "").strip()
    ]


def check_remote(site: Path, base_url: str) -> list[Finding]:
    findings: list[Finding] = []
    for url in sitemap_urls(site):
        status, final_url = request_status(url)
        if status != 200:
            findings.append(Finding("error", "sitemap.xml", url, f"deployed page returned HTTP {status}: {final_url}"))
        time.sleep(0.05)

    missing_url = urllib.parse.urljoin(base_url.rstrip("/") + "/", "__arsas_measurement_missing_page__.html")
    status, final_url = request_status(missing_url)
    if status != 404:
        findings.append(Finding("error", "404-probe", missing_url, f"expected HTTP 404, received {status}: {final_url}"))

    latest = json.loads((site / "latest.json").read_text(encoding="utf-8"))
    for key in ("installer", "portable", "checksums"):
        url = str(latest[key]["url"])
        status, final_url = request_status(url, timeout=45.0)
        if status != 200:
            findings.append(Finding("error", "latest.json", url, f"release asset returned HTTP {status}: {final_url}"))
    return findings


def write_reports(output: Path, findings: list[Finding], remote: bool) -> None:
    output.mkdir(parents=True, exist_ok=True)
    errors = [item for item in findings if item.severity == "error"]
    warnings = [item for item in findings if item.severity == "warning"]
    payload = {
        "schemaVersion": 1,
        "remoteChecked": remote,
        "errors": len(errors),
        "warnings": len(warnings),
        "findings": [asdict(item) for item in findings],
    }
    (output / "site-health.json").write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    lines = [
        "# ARSAS site health",
        "",
        f"- Internal/deployed errors: **{len(errors)}**",
        f"- Warnings: **{len(warnings)}**",
        f"- Remote deployment checked: **{'yes' if remote else 'no'}**",
        "",
    ]
    if findings:
        lines.extend(["## Findings", ""])
        for item in findings:
            lines.append(f"- **{item.severity.upper()}** `{item.source}` → `{item.target}` — {item.message}")
    else:
        lines.append("No broken local links, missing fragments, failed deployed pages or invalid 404 response were found.")
    (output / "site-health.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--site", default="_site")
    parser.add_argument("--output", default="_measurement")
    parser.add_argument("--remote", action="store_true")
    parser.add_argument("--base-url", default=CANONICAL_ROOT)
    args = parser.parse_args()

    site = Path(args.site).resolve()
    if not site.is_dir():
        raise SystemExit(f"Site directory does not exist: {site}")

    findings = check_local(site)
    if args.remote:
        findings.extend(check_remote(site, args.base_url))
    write_reports(Path(args.output).resolve(), findings, args.remote)

    errors = [item for item in findings if item.severity == "error"]
    if errors:
        print(f"ARSAS site health failed with {len(errors)} error(s).", file=sys.stderr)
        return 1
    print("ARSAS site health passed: links, fragments, deployed pages and 404 behavior are valid.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
