#!/usr/bin/env python3
"""Real Chromium smoke checks of the fully-rendered ARSAS product website.

The run is explicitly lab/headless evidence, NOT real mobile hardware, WCAG
certification, NVDA sign-off, or CrUX field Core Web Vitals.
"""
from __future__ import annotations

import argparse
import functools
import json
import threading
from datetime import datetime, timezone
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urljoin

from playwright.sync_api import sync_playwright

PAIRS = (
    ("index.html", "id.html"),
    ("download.html", "unduh.html"),
    ("quick-start.html", "panduan-mulai-arsas.html"),
    ("demo.html", "demo-arsas.html"),
    ("faq.html", "faq-arsas.html"),
    ("compatibility.html", "bukti-kompatibilitas.html"),
)
WIDTHS = (360, 390, 768, 1280, 1920)


class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self, *args: object) -> None:
        pass


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--site", type=Path, default=Path("_site"))
    parser.add_argument("--output", type=Path, default=Path("_browser-qa"))
    args = parser.parse_args()
    site = args.site.resolve()
    if not site.is_dir():
        parser.error(f"Site directory missing: {site}")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    server = ThreadingHTTPServer(
        ("127.0.0.1", 0),
        functools.partial(QuietHandler, directory=str(site)),
    )
    server.daemon_threads = True
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    root = f"http://127.0.0.1:{server.server_port}/"
    failures: list[str] = []
    rows: list[dict[str, object]] = []
    checked = 0
    try:
        with sync_playwright() as playwright:
            browser = playwright.chromium.launch(headless=True)
            try:
                for width in WIDTHS:
                    pages = PAIRS if width in (390, 1280) else PAIRS[:2]
                    for english, indonesian in pages:
                        for lang, path in (("en", english), ("id", indonesian)):
                            checked += 1
                            context = browser.new_context(
                                viewport={"width": width, "height": 850},
                                device_scale_factor=1,
                                reduced_motion="reduce",
                            )
                            page = context.new_page()
                            errors: list[str] = []
                            page.on("pageerror", lambda exc, error_list=errors: error_list.append(str(exc)))
                            try:
                                response = page.goto(urljoin(root, path), wait_until="load", timeout=30000)
                                if response is None or response.status != 200:
                                    raise AssertionError(f"HTTP {response.status if response else 'none'}")
                                actual_lang = page.locator("html").get_attribute("lang")
                                if actual_lang != lang:
                                    errors.append(f"html lang={actual_lang}, expected {lang}")
                                if page.locator("main#main").count() != 1:
                                    errors.append("missing main#main")
                                h1 = page.locator("main h1").count()
                                if h1 != 1:
                                    errors.append(f"expected one main h1, got {h1}")
                                if page.locator('a.skip-link[href="#main"]').count() != 1:
                                    errors.append("broken skip-to-content link")
                                overflow = page.evaluate(
                                    "() => ({page: document.documentElement.scrollWidth, viewport: innerWidth})"
                                )
                                if overflow["page"] > overflow["viewport"] + 2:
                                    offenders = page.evaluate("""() => [...document.querySelectorAll('body *')]
                                      .filter(el => el.getBoundingClientRect().right > innerWidth + 2)
                                      .slice(0, 8).map(el => ({
                                        tag: el.tagName.toLowerCase(),
                                        cls: (el.getAttribute('class') || '').slice(0, 70),
                                        right: Math.round(el.getBoundingClientRect().right),
                                        width: Math.round(el.getBoundingClientRect().width),
                                        label: (el.textContent || '').trim().slice(0, 55)
                                      }))""")
                                    errors.append(f"horizontal overflow {overflow['page']}>{overflow['viewport']}; offenders={offenders}")
                                    if path in ("download.html", "unduh.html") and width in (360, 390):
                                        page.screenshot(
                                            path=str(output / f"overflow-{path}-{width}px.png"),
                                            full_page=True, animations="disabled"
                                        )
                                if width < 1081:
                                    toggle = page.locator("button[data-menu-toggle]")
                                    if not toggle.is_visible():
                                        errors.append("mobile navigation toggle hidden")
                                    else:
                                        expected_open = "Buka navigasi" if lang == "id" else "Open navigation"
                                        expected_close = "Tutup navigasi" if lang == "id" else "Close navigation"
                                        if toggle.get_attribute("aria-label") != expected_open:
                                            errors.append("incorrect mobile menu accessible open name")
                                        toggle.click()
                                        if toggle.get_attribute("aria-expanded") != "true":
                                            errors.append("mobile menu did not open")
                                        if toggle.get_attribute("aria-label") != expected_close:
                                            errors.append("incorrect mobile menu accessible close name")
                                        page.keyboard.press("Escape")
                                        if toggle.get_attribute("aria-expanded") != "false":
                                            errors.append("Escape did not close menu")
                                        toggle.click()
                                        page.locator("nav[data-nav-links] a[data-nav-page='capabilities'] span[data-lang='" + lang + "']").click()
                                        if page.url.split("?")[0].split("/")[-1] != "features.html":
                                            errors.append("nested localized navigation click failed")
                                        if page.locator("button[data-menu-toggle]").get_attribute("aria-expanded") != "false":
                                            errors.append("nested link did not close menu")
                                else:
                                    if not page.locator("nav[data-nav-links]").is_visible():
                                        errors.append("desktop primary navigation hidden")
                                if width in (390, 1280) and path in ("index.html", "id.html"):
                                    screenshot_path = output / f"homepage-{lang}-{width}px.png"
                                    page.goto(urljoin(root, path), wait_until="load")
                                    page.screenshot(path=str(screenshot_path), full_page=True, animations="disabled")
                            except Exception as exc:
                                errors.append(f"{type(exc).__name__}: {exc}")
                            finally:
                                rows.append({"page": path, "width": width, "lang": lang, "errors": errors})
                                failures.extend(f"{path} at {width}px: {msg}" for msg in errors)
                                page.close()
                                context.close()
            finally:
                browser.close()
    finally:
        server.shutdown()
        server.server_close()
    report = {
        "scope": "headless Chromium lab browser smoke; not physical-mobile, NVDA, or field CWV",
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "checks": checked,
        "failures": len(failures),
        "cases": rows,
    }
    (output / "browser-qa.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    summary = [
        "# ARSAS public-site browser QA",
        "",
        f"- Browser: Playwright Chromium (headless)",
        f"- Widths: {', '.join(str(x) for x in WIDTHS)} px",
        f"- Page/viewport tests: **{checked}**",
        f"- Findings: **{len(failures)}**",
        "- Scope: viewport overflow, live JS, EN/ID header, headings/skip link, mobile menu Escape and nested-label clicks",
        "- Limitations: not NVDA, WCAG certification, physical device, Lighthouse or field Core Web Vitals",
        "",
    ]
    summary += ["## Findings", "", *[f"- {finding}" for finding in failures]] if failures else [
        "No test failures observed in this headless Chromium run."
    ]
    (output / "browser-qa.md").write_text("\n".join(summary) + "\n", encoding="utf-8")
    print("\n".join(summary))
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
