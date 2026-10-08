#!/usr/bin/env python3
"""P7.6 read-only qualification of one ARSAS full Copy Diagnostic snapshot.

Diagnostic consistency is NOT physical, wire, PCAP, R10 or release authority.
Input is kept local. Never upload unredacted customer endpoints or reports.
"""
from __future__ import annotations

import argparse
from datetime import datetime
import json
from pathlib import Path
import re
import sys

HEX64 = re.compile(r"^[0-9a-f]{64}$")
IED = re.compile(r"^IED\s+:\s*(.+?)\s*$", re.M)
FINGERPRINT = re.compile(r"^Static parity\s+:\s*MATCH\s+•\s*([0-9a-f]{64})\s*$", re.M)
TRAFFIC = re.compile(r"^Routed traffic\s+:\s*DUAL INGRESS TRAFFIC PROVEN\b", re.M)
INGRESS = re.compile(
    r"^\s{2}(Discovery|Open SCL)\s+:\s*(LiveDiscovery|OpenScl): "
    r"fingerprint=([0-9a-f]{64}), requested=(\d+), plans=(\d+), "
    r"staticBRCB=(\d+), staticURCB=(\d+), uncovered=(\d+)\s*$"
)
SUMMARY = re.compile(r"^\s{4}traffic\s+:\s*(\d+)/(\d+) exact static RCB target\(s\) routed; first=(\S+)\s*$")
TARGET = re.compile(
    r"^\s{4}runtime\s+:\s*(static-brcb|static-urcb)\|dataset=([^|]+)\|"
    r"rcb=([^|]+)\|bindings=(\d+) \[(ROUTED|PENDING)\]\s*$"
)
AP = re.compile(r"^SCD selected AP\s+:\s*(\S+)\s*$", re.M)
MAX_DIAGNOSTIC_TARGETS = 8


class QualificationError(ValueError):
    pass


def single(pattern: re.Pattern[str], data: str, name: str) -> re.Match[str]:
    found = list(pattern.finditer(data))
    if len(found) != 1:
        raise QualificationError(f"{name}: expected exactly one, found {len(found)}")
    return found[0]


def qualify(text: str, *, ied: str, min_brcb: int = 1,
            min_urcb: int = 1, selected_ap: str | None = None) -> dict:
    if not ied.strip() or "\n" in ied or min_brcb < 1 or min_urcb < 1:
        raise QualificationError("Exact IED and positive signal minimums required")
    if not text.startswith("ARSAS Diagnostic Report"):
        raise QualificationError("Full Copy Diagnostic required; emergency/partial summaries cannot qualify")
    matches = list(IED.finditer(text))
    matching = [(m, i) for i, m in enumerate(matches) if m.group(1) == ied]
    if len(matching) != 1:
        raise QualificationError("Exact IED identity missing or ambiguous")
    beginning, index = matching[0]
    end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
    section = text[beginning.start():end]
    parity = single(FINGERPRINT, section, "static semantic MATCH").group(1)
    single(TRAFFIC, section, "explicit dual-ingress routed traffic proof")
    if selected_ap is not None:
        if not selected_ap or single(AP, section, "selected AccessPoint").group(1) != selected_ap:
            raise QualificationError("SCD AccessPoint is not the explicitly requested AP")

    captures: dict[str, dict] = {}
    current: str | None = None
    for line in section.splitlines():
        found = INGRESS.fullmatch(line)
        if found:
            key, actual, fingerprint, requested, plans, brcb, urcb, uncovered = found.groups()
            if key in captures or (key == "Discovery" and actual != "LiveDiscovery") or (
                key == "Open SCL" and actual != "OpenScl"
            ):
                raise QualificationError("Duplicated or mismatched ingress identity")
            captures[key] = dict(fingerprint=fingerprint, requested=int(requested),
                                 plans=int(plans), brcb=int(brcb), urcb=int(urcb),
                                 uncovered=int(uncovered), targets=[], summary=None)
            current = key
            continue
        if re.match(r"^\s{2}(?:Discovery|Open SCL)\s+:", line):
            raise QualificationError("Malformed ingress header")
        hit = SUMMARY.fullmatch(line)
        if hit:
            if current is None or captures[current]["summary"] is not None:
                raise QualificationError("Missing or duplicate ingress traffic summary")
            captures[current]["summary"] = (int(hit.group(1)), int(hit.group(2)), hit.group(3))
            continue
        hit = TARGET.fullmatch(line)
        if hit:
            if current is None:
                raise QualificationError("Unowned runtime RCB target")
            kind, dataset, rcb, bindings, status = hit.groups()
            captures[current]["targets"].append((kind, dataset, rcb, int(bindings), status))
            continue
        if re.match(r"^\s{4}(?:runtime|traffic)\s+:", line):
            raise QualificationError("Unparseable target or traffic line; fail closed")

    if set(captures) != {"Discovery", "Open SCL"}:
        raise QualificationError("Both independent ingress summaries are required")

    details = {}
    for name, entry in captures.items():
        if entry["fingerprint"] != parity or not HEX64.fullmatch(entry["fingerprint"]):
            raise QualificationError(f"{name}: fingerprint does not match semantic MATCH")
        if entry["uncovered"] != 0 or entry["requested"] <= 0:
            raise QualificationError(f"{name}: uncovered or empty selection")
        if entry["brcb"] < min_brcb or entry["urcb"] < min_urcb:
            raise QualificationError(f"{name}: insufficient static BRCB/URCB signal coverage")
        if entry["summary"] is None:
            raise QualificationError(f"{name}: no routed-target count or first timestamp")
        routed, expected, when = entry["summary"]
        count = entry["plans"]
        # Copy Diagnostic intentionally truncates to eight lines; never assert
        # all target families are proven from a truncated or partial listing.
        if not (0 < count <= MAX_DIAGNOSTIC_TARGETS and count == expected == routed ==
                len(entry["targets"])):
            raise QualificationError(f"{name}: incomplete/truncated target inventory")
        try:
            time = datetime.fromisoformat(when.replace("Z", "+00:00"))
            if time.tzinfo is None:
                raise ValueError("timezone absent")
        except ValueError as exc:
            raise QualificationError(f"{name}: invalid routed-report timestamp") from exc
        targets = entry["targets"]
        if len({(k, ds, rc) for k, ds, rc, _, _ in targets}) != len(targets):
            raise QualificationError(f"{name}: duplicate RCB target")
        if any(not ds.strip() or not rc.strip() or n <= 0 or status != "ROUTED"
               for _, ds, rc, n, status in targets):
            raise QualificationError(f"{name}: target unbound, unrouted or empty")
        if {kind for kind, _, _, _, _ in targets} != {"static-brcb", "static-urcb"}:
            raise QualificationError(f"{name}: both BRCB and URCB must route independently")
        details[name] = {
            "plannedTargets": count,
            "routedTargets": len(targets),
            "brcbSignals": entry["brcb"],
            "urcbSignals": entry["urcb"],
            "firstRoutedUtc": time.isoformat(),
            "rcbFamilies": sorted({kind for kind, _, _, _, _ in targets}),
        }
    return {
        "ied": ied,
        "semanticFingerprint": parity,
        "selectedAccessPoint": selected_ap,
        "qualification": "DIAGNOSTIC_CONSISTENCY_PASS_NOT_PHYSICAL_VERIFIED",
        "physicalVerification": False,
        "wirePcapVerification": False,
        "discovery": details["Discovery"],
        "openScl": details["Open SCL"],
        "note": "A full current-session Copy Diagnostic is not independent PCAP/IED authority.",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--diagnostic", type=Path, required=True)
    parser.add_argument("--ied", required=True, help="Exact IED name from Copy Diagnostic")
    parser.add_argument("--selected-ap", help="Optional exact SCD AccessPoint from the capture")
    parser.add_argument("--min-brcb-signals", type=int, default=1)
    parser.add_argument("--min-urcb-signals", type=int, default=1)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        report = qualify(args.diagnostic.read_text(encoding="utf-8-sig"),
                         ied=args.ied, selected_ap=args.selected_ap,
                         min_brcb=args.min_brcb_signals, min_urcb=args.min_urcb_signals)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n",
                               encoding="utf-8")
        print("P7.6 diagnostic consistency PASS; PHYSICAL VERIFICATION NOT ESTABLISHED")
        return 0
    except (OSError, UnicodeError, QualificationError) as exc:
        print(f"P7.6 qualification not established: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
