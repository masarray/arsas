#!/usr/bin/env python3
"""Verify that public GitHub Pages serves the expected ARSAS build."""

from __future__ import annotations

import argparse
import json
import re
import time
from datetime import datetime, timezone
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urljoin
from urllib.request import Request, urlopen

MEASUREMENT_CONFIG_PATTERN = re.compile(
    r'<script\s+id="arsas-analytics"\s+type="application/json"\s+data-measurement-id="([^"]*)"\s+data-stable-version="([^"]+)"\s*>\s*</script>',
    re.IGNORECASE,
)
MEASUREMENT_ID_PATTERN = re.compile(r"G-[A-Z0-9]+")


def parse_bool(value: str) -> bool:
    normalized = value.strip().lower()
    if normalized in {"1", "true", "yes", "enabled"}:
        return True
    if normalized in {"0", "false", "no", "disabled"}:
        return False
    raise argparse.ArgumentTypeError("expected true or false")


def fetch(url: str, timeout: int = 20) -> tuple[int, str, dict[str, str]]:
    request = Request(
        url,
        headers={
            "User-Agent": "ARSAS-Pages-Attestation/1.1",
            "Accept": "application/json,text/html;q=0.9,*/*;q=0.8",
            "Cache-Control": "no-cache",
            "Pragma": "no-cache",
        },
    )
    try:
        with urlopen(request, timeout=timeout) as response:
            return response.status, response.read().decode("utf-8", errors="replace"), dict(response.headers.items())
    except HTTPError as exc:
        return exc.code, exc.read().decode("utf-8", errors="replace"), dict(exc.headers.items())
    except URLError as exc:
        raise RuntimeError(str(exc.reason)) from exc


def validate_inert_measurement_config(page: str, measurement_enabled: bool, stable_version: str, label: str) -> list[str]:
    errors: list[str] = []
    matches = MEASUREMENT_CONFIG_PATTERN.findall(page)
    if len(matches) != 1:
        return [f"{label} must contain exactly one inert analytics availability configuration"]
    measurement_id, page_stable_version = matches[0]
    if bool(MEASUREMENT_ID_PATTERN.fullmatch(measurement_id)) is not measurement_enabled:
        errors.append(f"{label} measurement availability does not match the deployed repository variable")
    if page_stable_version != stable_version:
        errors.append(f"{label} stable-version context is {page_stable_version!r}, expected {stable_version}")
    return errors


def check_once(base_url: str, source_commit: str, stable_version: str, measurement_enabled: bool, nonce: str) -> tuple[list[str], dict[str, object]]:
    errors: list[str] = []
    evidence: dict[str, object] = {}
    info_url = urljoin(base_url, "build-info.json") + "?" + urlencode({"attest": source_commit, "n": nonce})
    status, body, headers = fetch(info_url)
    evidence["buildInfoUrl"] = info_url
    evidence["buildInfoStatus"] = status
    evidence["buildInfoHeaders"] = {
        key: headers[key]
        for key in headers
        if key.lower() in {"etag", "last-modified", "cache-control", "content-type"}
    }
    if status != 200:
        return [f"build-info.json returned HTTP {status}"], evidence
    try:
        info = json.loads(body)
    except json.JSONDecodeError as exc:
        return [f"build-info.json is not valid JSON: {exc}"], evidence
    evidence["publicBuildInfo"] = info

    if info.get("sourceCommit") != source_commit:
        errors.append(f"public sourceCommit is {info.get('sourceCommit')!r}, expected {source_commit}")
    attestation = info.get("deploymentAttestation")
    if not isinstance(attestation, dict) or attestation.get("sourceCommit") != source_commit:
        errors.append("deploymentAttestation does not match the expected source commit")
    if info.get("stableReleaseVersion") != stable_version:
        errors.append(f"public stable release is {info.get('stableReleaseVersion')!r}, expected {stable_version}")
    if not info.get("workflowRunId") or not info.get("buildTimestampUtc"):
        errors.append("public build metadata is missing workflowRunId or buildTimestampUtc")

    measurement = info.get("measurement")
    if not isinstance(measurement, dict):
        errors.append("public build is missing measurement metadata")
    else:
        if measurement.get("enabled") is not measurement_enabled:
            errors.append(f"public measurement enabled={measurement.get('enabled')!r}, expected {measurement_enabled}")
        if measurement.get("consentRequired") is not True or measurement.get("defaultConsent") != "denied":
            errors.append("public measurement metadata does not require denied-by-default consent")
        if measurement.get("advertisingSignals") is not False or measurement.get("adPersonalizationSignals") is not False:
            errors.append("public measurement metadata must keep advertising and personalization signals disabled")

    privacy_pages = info.get("privacyPages")
    if privacy_pages != ["privacy.html", "privasi.html"]:
        errors.append("public build does not declare both bilingual privacy pages")
    privacy = info.get("privacy")
    if not isinstance(privacy, dict):
        errors.append("public privacy metadata is missing")
    else:
        if privacy.get("consentRequired") is not True or privacy.get("defaultAnalyticsConsent") != "denied":
            errors.append("public privacy metadata does not require denied-by-default consent")
        if privacy.get("measurementAvailable") is not measurement_enabled:
            errors.append("public privacy measurement availability does not match deployment configuration")
        if privacy.get("analyticsClientLoadedOnPolicyPages") is not False:
            errors.append("public privacy metadata must prohibit analytics client loading on policy pages")

    authority = info.get("searchAuthority")
    if not isinstance(authority, dict):
        errors.append("public build is missing search-authority metadata")
    else:
        if authority.get("schemaVersion") != 1:
            errors.append("public search-authority schema is invalid")
        if authority.get("allGuidesHaveCapabilityOrSolutionInbound") is not True:
            errors.append("public search-authority metadata does not prove guide discovery coverage")
        if int(authority.get("mappedPageCount", 0) or 0) < 20:
            errors.append("public search-authority graph maps too few pages")
        if int(authority.get("relationshipCount", 0) or 0) < 60:
            errors.append("public search-authority graph has too few contextual relationships")

    for relative in ("privacy.html", "privasi.html"):
        url = urljoin(base_url, relative) + "?" + urlencode({"attest": source_commit, "n": nonce})
        page_status, page, _ = fetch(url)
        evidence[f"{relative}Status"] = page_status
        if page_status != 200:
            errors.append(f"{relative} returned HTTP {page_status}")
            continue
        for required in (
            'name="robots" content="noindex,follow"',
            'data-privacy-page="true"',
            'src="consent.js"',
            'data-consent-manage',
            'data-consent-status',
        ):
            if required not in page:
                errors.append(f"{relative} is missing {required}")
        errors.extend(validate_inert_measurement_config(page, measurement_enabled, stable_version, relative))
        if 'src="analytics.js"' in page or "googletagmanager.com" in page:
            errors.append(f"{relative} must never load an analytics client")

    home_url = base_url + "?" + urlencode({"attest": source_commit, "n": nonce})
    home_status, home, _ = fetch(home_url)
    evidence["homeStatus"] = home_status
    if home_status != 200:
        errors.append(f"homepage returned HTTP {home_status}")
    else:
        for required in ('src="consent.js"', 'id="arsas-analytics"', 'data-consent-banner', 'assets/app-icon.png'):
            if required not in home:
                errors.append(f"homepage is missing {required}")
        errors.extend(validate_inert_measurement_config(home, measurement_enabled, stable_version, "homepage"))
        if 'src="analytics.js"' in home or "googletagmanager.com" in home:
            errors.append("homepage must not load analytics before consent")

    authority_url = urljoin(base_url, "smart-reporting.html") + "?" + urlencode({"attest": source_commit, "n": nonce})
    authority_status, authority_page, _ = fetch(authority_url)
    evidence["authorityProbeStatus"] = authority_status
    if authority_status != 200:
        errors.append(f"smart-reporting.html returned HTTP {authority_status}")
    else:
        for required in (
            'data-search-authority="reporting"',
            'href="reporting-silent.html"',
            'href="brcb-vs-urcb.html"',
            'href="rcb-reserved.html"',
            'href="empty-dataset.html"',
            'data-section="download-cta"',
        ):
            if required not in authority_page:
                errors.append(f"smart-reporting.html is missing contextual authority evidence {required}")
        if authority_page.find('data-search-authority="reporting"') > authority_page.find('data-section="download-cta"') >= 0:
            errors.append("smart-reporting.html authority section appears after the download CTA")

    graph_url = urljoin(base_url, "search-authority.json") + "?" + urlencode({"attest": source_commit, "n": nonce})
    graph_status, _, _ = fetch(graph_url)
    evidence["privateAuthoritySourceStatus"] = graph_status
    if graph_status != 404:
        errors.append(f"search-authority.json must not be publicly deployed; observed HTTP {graph_status}")

    return errors, evidence


def write_report(path: Path, success: bool, attempts: int, evidence: dict[str, object], errors: list[str]) -> None:
    payload = {
        "schemaVersion": 3,
        "verifiedAtUtc": datetime.now(timezone.utc).isoformat(),
        "success": success,
        "attempts": attempts,
        "errors": errors,
        "evidence": evidence,
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.with_suffix(".json").write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    lines = [
        "# ARSAS production deployment attestation",
        "",
        f"- Status: **{'PASS' if success else 'FAIL'}**",
        f"- Attempts: {attempts}",
    ]
    if errors:
        lines.extend(["", "## Last observed errors", "", *[f"- {error}" for error in errors]])
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--stable-version", required=True)
    parser.add_argument("--measurement-enabled", type=parse_bool, required=True)
    parser.add_argument("--attempts", type=int, default=24)
    parser.add_argument("--delay", type=float, default=10.0)
    parser.add_argument("--output", default="_validation/production-attestation.md")
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/") + "/"
    source_commit = args.source_commit.strip().lower()
    last_errors: list[str] = []
    last_evidence: dict[str, object] = {}
    used_attempts = 0
    for attempt in range(1, max(1, args.attempts) + 1):
        used_attempts = attempt
        try:
            last_errors, last_evidence = check_once(
                base_url,
                source_commit,
                args.stable_version.strip(),
                args.measurement_enabled,
                str(attempt),
            )
        except Exception as exc:
            last_errors = [f"network verification failed: {exc}"]
            last_evidence = {}
        if not last_errors:
            write_report(Path(args.output), True, used_attempts, last_evidence, [])
            print(f"Public ARSAS Pages attestation passed for {source_commit} after {used_attempts} attempt(s).")
            return 0
        print(f"Attempt {attempt}/{args.attempts}: " + "; ".join(last_errors))
        if attempt < args.attempts:
            time.sleep(max(0.0, args.delay))

    write_report(Path(args.output), False, used_attempts, last_evidence, last_errors)
    print("Public ARSAS Pages attestation failed: " + "; ".join(last_errors))
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
