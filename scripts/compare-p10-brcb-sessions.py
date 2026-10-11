#!/usr/bin/env python3
"""Offline diagnostic comparison for exact BRCB slots. No IEC/MMS I/O.

Observational scope only. Even matching SqNum/EntryID-relation chronologies
cannot prove SOE continuity, event delivery or original wire PDU ordering.
"""
from __future__ import annotations

import argparse
import json
import re
from dataclasses import asdict, dataclass
from pathlib import Path

STREAM = re.compile(r"^\s*stream\[\d+\]\s*:\s*(BRCB|URCB),\s*rcb=([^,\r\n]+),\s*dataset=([^,\r\n]+),.*\bframes=(\d+),")
TRIAGE = re.compile(r"^\s*RCB triage\s*:\s*anomalousFrames=(\d+),\s*sqDiscontinuities=(\d+),")
FOLLOW = re.compile(r"^\s*RCB follow-up\s*:\s*receivedAfterLastFinding=(\d+),")
FRAME = re.compile(r"^\s*(?:frame|anomalyFrame)#(\d+):\s*receivedUtc=([^,\s]+),\s*SqNum=([^,\s]+),\s*GIreason=([^,\s]+),\s*BufOvfl=([^,\s]+),\s*EntryIDrelation=([^,\s]+),\s*findings=(\d+)")
FINGERPRINT = re.compile(r"\bfingerprint=([a-fA-F0-9]{64})\b")
IED = re.compile(r"^\s*IED\s*:\s*([^\r\n]+)$")
ENGINE = re.compile(r"^Engine version\s*:\s*[^+\r\n]+\+([0-9a-f]{40})\s*$")
DATASET = re.compile(r"^\s*Static DataSets\s*:\s*(\d+)")

@dataclass(frozen=True)
class Stream:
    kind: str
    rcb: str
    dataset: str
    frames: int
    anomalous_frames: int = 0
    discontinuities: int = 0
    after_last_finding: int | None = None
    chronology: tuple[tuple[str, ...], ...] = ()
    recent_anomalies: tuple[tuple[str, ...], ...] = ()

def parse_report(report: str) -> dict:
    streams: dict[str, dict] = {}
    active: str | None = None
    fingerprint: str | None = None
    ied: str | None = None
    engine: str | None = None
    for line in report.splitlines():
        m = FINGERPRINT.search(line)
        if m and line.lstrip().startswith(("Discovery", "Open SCL", "LiveDiscovery:")):
            fingerprint = m.group(1).lower()
        m = IED.match(line)
        if m:
            ied = m.group(1).strip()
        m = ENGINE.match(line)
        if m:
            engine = m.group(1)
        m = STREAM.match(line)
        if m:
            kind, rcb, dataset, count = m.groups()
            active = rcb.strip().casefold()
            if active in streams:
                raise ValueError(f"duplicate RCB in diagnostic: {active}")
            streams[active] = dict(kind=kind, rcb=rcb.strip(), dataset=dataset.strip(),
                                   frames=int(count), anomalous_frames=0, discontinuities=0,
                                   after_last_finding=None, chronology=[], recent_anomalies=[])
            continue
        if active is None or active not in streams:
            continue
        m = TRIAGE.match(line)
        if m:
            streams[active]["anomalous_frames"] = int(m.group(1))
            streams[active]["discontinuities"] = int(m.group(2))
        m = FOLLOW.match(line)
        if m:
            streams[active]["after_last_finding"] = int(m.group(1))
        m = FRAME.match(line)
        if m:
            values = m.groups()
            key = "recent_anomalies" if "anomalyFrame#" in line else "chronology"
            streams[active][key].append(values)
    if not ied or not fingerprint or not engine or not streams:
        raise ValueError("missing IED, engine SHA, static fingerprint, or RCB streams")
    return dict(ied=ied, fingerprint=fingerprint, engine=engine,
                streams={k: Stream(**{**v, "chronology": tuple(v["chronology"]),
                                       "recent_anomalies": tuple(v["recent_anomalies"])})
                         for k, v in streams.items()})

def compare(left: dict, right: dict) -> dict:
    if left["ied"].casefold() != right["ied"].casefold():
        raise ValueError("different IED identities; comparison blocked")
    if left["fingerprint"] != right["fingerprint"]:
        raise ValueError("different static semantic fingerprints; comparison blocked")
    if left["engine"] != right["engine"]:
        raise ValueError("different engine commits; comparison blocked")
    la = {k: s for k, s in left["streams"].items() if s.kind == "BRCB"}
    ra = {k: s for k, s in right["streams"].items() if s.kind == "BRCB"}
    if not la or not ra:
        raise ValueError("no BRCB streams")
    if set(la) != set(ra):
        raise ValueError("different BRCB slots: no cross-slot reliability comparison")
    results = []
    for ref in sorted(la):
        a, b = la[ref], ra[ref]
        if a.dataset.casefold() != b.dataset.casefold():
            raise ValueError(f"different DataSet on BRCB {ref}; comparison blocked")
        results.append(dict(rcb=a.rcb, dataset=a.dataset,
                            left_frames=a.frames, right_frames=b.frames,
                            left_findings_frames=a.anomalous_frames,
                            right_findings_frames=b.anomalous_frames,
                            left_sq_discontinuities=a.discontinuities,
                            right_sq_discontinuities=b.discontinuities,
                            left_clean_followup=a.after_last_finding,
                            right_clean_followup=b.after_last_finding,
                            left_chronology_frames=len(a.chronology),
                            right_chronology_frames=len(b.chronology),
                            left_anomaly_history_frames=len(a.recent_anomalies),
                            right_anomaly_history_frames=len(b.recent_anomalies)))
    return dict(status="SAME_SLOT_OBSERVATION_ONLY", ied=left["ied"],
                fingerprint=left["fingerprint"], engine_commit=left["engine"],
                slots=results,
                qualification="NOT_WIRE_ORDER_PROOF; NOT_SOE_LOSSLESS; NOT_R10_PHYSICAL_ACCEPTANCE")

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("open_scl", type=Path, help="Open SCL ARSAS Diagnostic Report")
    ap.add_argument("discovery", type=Path, help="Discovery IP ARSAS Diagnostic Report")
    ap.add_argument("--output", type=Path, help="optional JSON output path")
    args = ap.parse_args()
    try:
        a = parse_report(args.open_scl.read_text(encoding="utf-8-sig"))
        b = parse_report(args.discovery.read_text(encoding="utf-8-sig"))
        result = compare(a, b)
    except (OSError, UnicodeError, ValueError) as exc:
        ap.exit(2, f"INCOMPARABLE: {exc}\n")
    formatted = json.dumps(result, indent=2, sort_keys=True)
    if args.output:
        args.output.write_text(formatted + "\n", encoding="utf-8")
    print(formatted)
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
