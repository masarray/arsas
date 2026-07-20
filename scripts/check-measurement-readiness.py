#!/usr/bin/env python3
"""Report whether ARSAS public collection and private read-only measurement are ready."""

from __future__ import annotations

import argparse
import base64
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

CANONICAL_ROOT = "https://masarray.github.io/arsas/"
MEASUREMENT_ID = re.compile(r"G-[A-Z0-9]+")
PROPERTY_ID = re.compile(r"[0-9]+")


def env(name: str) -> str:
    return os.environ.get(name, "").strip()


def decode_service_account(raw: str) -> dict[str, Any] | None:
    if not raw:
        return None
    try:
        if raw.startswith("base64:"):
            raw = base64.b64decode(raw.removeprefix("base64:")).decode("utf-8")
        value = json.loads(raw)
    except Exception as exc:
        raise ValueError(f"GOOGLE_SERVICE_ACCOUNT_JSON cannot be decoded: {exc}") from exc
    if not isinstance(value, dict) or value.get("type") != "service_account":
        raise ValueError("GOOGLE_SERVICE_ACCOUNT_JSON is not a service-account JSON object")
    if not value.get("client_email") or not value.get("private_key"):
        raise ValueError("GOOGLE_SERVICE_ACCOUNT_JSON is missing client_email or private_key")
    return value


def write_output(name: str, value: str) -> None:
    target = env("GITHUB_OUTPUT")
    if not target:
        return
    with open(target, "a", encoding="utf-8") as handle:
        handle.write(f"{name}={value}\n")


def markdown(payload: dict[str, Any]) -> str:
    checks = payload["checks"]
    lines = [
        "# ARSAS measurement activation readiness",
        "",
        f"Generated: `{payload['generatedAtUtc']}`",
        "",
        f"- Overall state: **{payload['state']}**",
        f"- Public consent-gated GA4 collection: **{'ready' if checks['clientCollectionReady'] else 'inactive'}**",
        f"- Private GA4 reporting: **{'ready' if checks['ga4ReportingReady'] else 'inactive'}**",
        f"- Private Search Console reporting: **{'ready' if checks['searchConsoleReady'] else 'inactive'}**",
        f"- Authentication mode: **{payload['authenticationMode']}**",
        "",
        "## Required repository configuration",
        "",
        "| Setting | Status | Purpose |",
        "|---|---|---|",
        f"| `GA4_MEASUREMENT_ID` | {checks['measurementIdStatus']} | Consent-gated browser collection |",
        f"| `GA4_PROPERTY_ID` | {checks['propertyIdStatus']} | Read-only GA4 Data API reporting |",
        f"| `GSC_SITE_URL` | {checks['searchConsoleSiteStatus']} | Exact Search Console property |",
        f"| Google read-only credentials | {checks['credentialStatus']} | GA4 and Search Console API access |",
        "",
    ]
    if payload["warnings"]:
        lines.extend(["## Warnings", "", *[f"- {item}" for item in payload["warnings"]], ""])
    if payload["errors"]:
        lines.extend(["## Blocking configuration errors", "", *[f"- {item}" for item in payload["errors"]], ""])
    lines.extend([
        "## Activation sequence",
        "",
        "1. Configure the exact Search Console URL-prefix property and grant read-only access.",
        "2. Configure the numeric GA4 property ID and the public web-stream measurement ID.",
        "3. Store the service-account JSON only as a GitHub Actions secret; never commit it.",
        "4. Run the measurement workflow manually and confirm that GA4 and Search Console both report `available`.",
        "5. Keep analytics denied by default; browser collection starts only after visitor consent.",
        "",
        "This report contains configuration state only. It never prints a private key, access token or service-account email.",
        "",
    ])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="_measurement/readiness")
    parser.add_argument("--strict", action="store_true", help="Fail on invalid or partially inconsistent configuration")
    args = parser.parse_args()

    measurement_id = env("GA4_MEASUREMENT_ID").upper()
    property_id = env("GA4_PROPERTY_ID")
    search_site = env("GSC_SITE_URL") or CANONICAL_ROOT
    service_raw = env("GOOGLE_SERVICE_ACCOUNT_JSON")
    adc_path = env("GOOGLE_APPLICATION_CREDENTIALS")
    wif_provider = env("GCP_WORKLOAD_IDENTITY_PROVIDER")
    wif_service_account = env("GCP_SERVICE_ACCOUNT")

    errors: list[str] = []
    warnings: list[str] = []

    measurement_valid = bool(MEASUREMENT_ID.fullmatch(measurement_id)) if measurement_id else False
    property_valid = bool(PROPERTY_ID.fullmatch(property_id)) if property_id else False
    search_valid = search_site == CANONICAL_ROOT

    if measurement_id and not measurement_valid:
        errors.append("GA4_MEASUREMENT_ID must use the public web-stream form G-XXXXXXXXXX.")
    if property_id and not property_valid:
        errors.append("GA4_PROPERTY_ID must contain the numeric GA4 property ID only.")
    if not search_valid:
        errors.append(f"GSC_SITE_URL must exactly match the verified URL-prefix property {CANONICAL_ROOT}.")
    if bool(wif_provider) != bool(wif_service_account):
        errors.append("GCP_WORKLOAD_IDENTITY_PROVIDER and GCP_SERVICE_ACCOUNT must be configured together.")

    service_account = None
    if service_raw:
        try:
            service_account = decode_service_account(service_raw)
        except ValueError as exc:
            errors.append(str(exc))

    adc_available = bool(adc_path and Path(adc_path).is_file())
    wif_declared = bool(wif_provider and wif_service_account)
    if service_account:
        auth_mode = "service-account-secret"
    elif adc_available:
        auth_mode = "workload-identity-adc"
    elif wif_declared:
        auth_mode = "workload-identity-declared"
    else:
        auth_mode = "not-configured"
    credential_ready = bool(service_account or adc_available)

    client_ready = measurement_valid
    ga4_ready = credential_ready and property_valid
    search_ready = credential_ready and search_valid
    live_ready = ga4_ready or search_ready
    fully_ready = client_ready and ga4_ready and search_ready

    configured_signals = any((measurement_id, property_id, service_raw, adc_path, wif_provider, wif_service_account))
    if not configured_signals:
        state = "disabled"
    elif errors:
        state = "invalid"
    elif fully_ready:
        state = "fully-ready"
    elif live_ready:
        state = "private-reporting-ready"
    elif client_ready:
        state = "collection-only"
    else:
        state = "partial"

    if client_ready and not live_ready:
        warnings.append("Public collection can be enabled, but private GA4/Search Console reporting is not yet authenticated.")
    if live_ready and not client_ready:
        warnings.append("Private read-only reporting is ready, but browser GA4 collection remains disabled.")
    if property_valid and not credential_ready:
        warnings.append("GA4_PROPERTY_ID is present but Google read-only credentials are unavailable.")
    if wif_declared and not adc_available:
        warnings.append("Workload Identity is declared but no ADC credential file is available in this step; run the Google auth action first.")
    if service_account and wif_declared:
        warnings.append("Both service-account JSON and Workload Identity are configured; prefer one authentication path to reduce ambiguity.")

    checks = {
        "clientCollectionReady": client_ready,
        "ga4ReportingReady": ga4_ready,
        "searchConsoleReady": search_ready,
        "liveReportingReady": live_ready,
        "fullyReady": fully_ready,
        "measurementIdStatus": "valid" if measurement_valid else ("invalid" if measurement_id else "missing"),
        "propertyIdStatus": "valid" if property_valid else ("invalid" if property_id else "missing"),
        "searchConsoleSiteStatus": "valid" if search_valid else "invalid",
        "credentialStatus": "available" if credential_ready else ("declared-not-active" if wif_declared else "missing"),
    }
    payload = {
        "schemaVersion": 1,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "state": state,
        "authenticationMode": auth_mode,
        "checks": checks,
        "warnings": warnings,
        "errors": errors,
    }

    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    (output / "readiness.json").write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    (output / "readiness.md").write_text(markdown(payload), encoding="utf-8")

    write_output("state", state)
    write_output("client_ready", str(client_ready).lower())
    write_output("ga4_ready", str(ga4_ready).lower())
    write_output("search_ready", str(search_ready).lower())
    write_output("live_ready", str(live_ready).lower())
    write_output("fully_ready", str(fully_ready).lower())

    print(
        "ARSAS measurement readiness: "
        f"state={state}, client={client_ready}, ga4={ga4_ready}, search={search_ready}, auth={auth_mode}."
    )
    if args.strict and (errors or state == "partial"):
        return 1
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"ARSAS measurement readiness failed: {exc}", file=sys.stderr)
        raise
