#!/usr/bin/env python3
"""Offline diagnostic comparison for exact BRCB slots. No IEC/MMS I/O.

Observational scope only. Even matching SqNum/EntryID-relation chronologies
cannot prove SOE continuity, event delivery or original wire PDU ordering.
"""
from __future__ import annotations

import argparse
import json
import re
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

STREAM = re.compile(r"^\s*stream\[\d+\]\s*:\s*(BRCB|URCB),\s*rcb=([^,\r\n]+),\s*dataset=([^,\r\n]+),.*\bframes=(\d+),")
TRIAGE = re.compile(r"^\s*RCB triage\s*:\s*anomalousFrames=(\d+),\s*sqDiscontinuities=(\d+),")
FOLLOW = re.compile(r"^\s*RCB follow-up\s*:\s*receivedAfterLastFinding=(\d+),")
FRAME = re.compile(r"^\s*(?:frame|anomalyFrame)#(\d+):\s*receivedUtc=([^,\s]+),\s*SqNum=([^,\s]+),\s*GIreason=([^,\s]+),\s*BufOvfl=([^,\s]+),\s*EntryIDrelation=([^,\s]+),\s*findings=(\d+)")
FINGERPRINT = re.compile(r"\bfingerprint=([a-fA-F0-9]{64})\b")
IED = re.compile(r"^\s*IED\s*:\s*([^\r\n]+)$")
ENGINE = re.compile(r"^Engine version\s*:\s*[^+\r\n]+\+([0-9a-f]{40})\s*$")
DATASET = re.compile(r"^\s*Static DataSets\s*:\s*(\d+)")
BUFOVFL = re.compile(r"(?:^|,)\s*BufOvfl=(\d+)(?:,|$)")
SQ_GI = re.compile(r"\bsqWithGIreason=(\d+)\b")
LIFECYCLE = re.compile(r"^\s*RCB lifecycle\s*:")
CHRONOLOGY = re.compile(r"^\s*BRCB chronology:\s*first=(\d+),\s*recent=(\d+),\s*omittedMiddle=(\d+)")

def _field(line: str, key: str) -> str | None:
    match = re.search(rf"(?:^|,\s*){re.escape(key)}=([^,;\s]+)", line)
    value = match.group(1) if match else None
    return value if value and value != "-" else None

@dataclass(frozen=True)
class Stream:
    kind: str
    rcb: str
    dataset: str
    frames: int
    overflow_frames: int = 0
    anomalous_frames: int = 0
    discontinuities: int = 0
    after_last_finding: int | None = None
    sq_with_gi_reason: int | None = None
    gi_reason_frames: int | None = None
    start_return_utc: str | None = None
    last_anomaly_utc: str | None = None
    chronology: tuple[tuple[str, ...], ...] = ()
    chronology_declared: tuple[int, int, int] | None = None
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
            overflow_match = BUFOVFL.search(line)
            if kind == "BRCB" and overflow_match is None:
                raise ValueError(f"missing BRCB BufOvfl count for {rcb.strip()}")
            overflow_frames = int(overflow_match.group(1)) if overflow_match else 0
            if overflow_frames > int(count):
                raise ValueError(f"BRCB BufOvfl count exceeds received frames for {rcb.strip()}")
            active = rcb.strip().casefold()
            if active in streams:
                raise ValueError(f"duplicate RCB in diagnostic: {active}")
            streams[active] = dict(kind=kind, rcb=rcb.strip(), dataset=dataset.strip(),
                                   frames=int(count), overflow_frames=overflow_frames,
                                   anomalous_frames=0, discontinuities=0,
                                   after_last_finding=None, sq_with_gi_reason=None,
                                   gi_reason_frames=None, start_return_utc=None,
                                   last_anomaly_utc=None, chronology=[],
                                   chronology_declared=None, recent_anomalies=[])
            continue
        if active is None or active not in streams:
            continue
        m = TRIAGE.match(line)
        if m:
            streams[active]["anomalous_frames"] = int(m.group(1))
            streams[active]["discontinuities"] = int(m.group(2))
            gi = SQ_GI.search(line)
            streams[active]["sq_with_gi_reason"] = int(gi.group(1)) if gi else None
        m = FOLLOW.match(line)
        if m:
            streams[active]["after_last_finding"] = int(m.group(1))
        if LIFECYCLE.match(line):
            count = _field(line, "GIreasonFrames")
            streams[active]["gi_reason_frames"] = int(count) if count is not None and count.isdecimal() else None
            streams[active]["start_return_utc"] = _field(line, "startReturnUtc")
            streams[active]["last_anomaly_utc"] = _field(line, "lastAnomalyUtc")
        m = CHRONOLOGY.match(line)
        if m and streams[active]["kind"] == "BRCB":
            streams[active]["chronology_declared"] = tuple(map(int, m.groups()))
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

def _require_matching_authority(left: dict, right: dict) -> None:
    if left["ied"].casefold() != right["ied"].casefold():
        raise ValueError("different IED identities; comparison blocked")
    if left["fingerprint"] != right["fingerprint"]:
        raise ValueError("different static semantic fingerprints; comparison blocked")
    if left["engine"] != right["engine"]:
        raise ValueError("different engine commits; comparison blocked")

def _elapsed_ms(start: str | None, end: str | None) -> float | None:
    if not start or not end:
        return None
    try:
        a, b = datetime.fromisoformat(start.replace("Z", "+00:00")), datetime.fromisoformat(end.replace("Z", "+00:00"))
        delta = (b - a).total_seconds() * 1000
        return round(delta, 3) if 0 <= delta <= 86400000 else None
    except (TypeError, ValueError):
        return None

def _receive_order_gap_evidence(stream: Stream) -> dict:
    """Only a fully retained decoder receive chronology authorizes temporal inferences.

    Neither callback timestamps nor SqNum/EntryID establish MMS packet order or SOE delivery.
    An invalid, bounded, omitted or untimed chronology returns unknown, not zero.
    """
    rows = stream.chronology
    declared = stream.chronology_declared
    reason = None
    if declared is None:
        reason = "MISSING_CHRONOLOGY_DECLARATION"
    elif declared[2] != 0 or sum(declared[:2]) != stream.frames:
        reason = "CHRONOLOGY_HAS_OMITTED_OR_UNACCOUNTED_FRAMES"
    elif len(rows) != stream.frames or len(rows) != sum(declared[:2]):
        reason = "RETAINED_CHRONOLOGY_INCOMPLETE"
    elif [int(row[0]) for row in rows] != list(range(1, stream.frames + 1)):
        reason = "RECEIVE_FRAME_INDICES_NOT_CONTIGUOUS"
    times = []
    if reason is None:
        for row in rows:
            try:
                moment = datetime.fromisoformat(row[1].replace("Z", "+00:00"))
                if moment.tzinfo is None or moment.utcoffset() is None:
                    raise ValueError("no UTC offset")
            except (TypeError, ValueError, OverflowError):
                reason = "MISSING_OR_INVALID_RECEIVE_TIMESTAMPS"
                break
            times.append(moment)
        if reason is None and any(b < a for a, b in zip(times, times[1:])):
            reason = "NONMONOTONIC_DECODER_RECEIVE_TIMESTAMPS"
    if reason is not None:
        return dict(coverage="UNVERIFIABLE", reason=reason,
                    recorded_frames=len(rows), expected_frames=stream.frames,
                    longest_receive_gap_ms=None,
                    non_gi_backward_finding_after_gap=None)
    gaps = [(b - a).total_seconds() * 1000 for a, b in zip(times, times[1:])]
    candidates = []
    for index, gap in enumerate(gaps, start=1):
        before, after = rows[index - 1], rows[index]
        try:
            prev_sq, curr_sq = int(before[2]), int(after[2])
        except ValueError:
            continue
        # A genuine decoded finding that recurs without GI or BufOvfl on this
        # received frame; this is NOT evidence of harmless replay or lost SOE.
        if (after[3] == "NO" and after[4] == "NO" and
                int(after[6]) > 0 and curr_sq <= prev_sq):
            candidates.append(dict(previous_frame=int(before[0]), frame=int(after[0]),
                                   previous_sqnum=prev_sq, sqnum=curr_sq,
                                   gap_ms=round(gap, 3),
                                   elapsed_since_start_return_ms=_elapsed_ms(
                                       stream.start_return_utc, after[1]),
                                   received_utc=after[1],
                                   entry_id_relation=after[5]))
    strongest = max(candidates, key=lambda x: x["gap_ms"]) if candidates else None
    return dict(coverage="COMPLETE_BOUNDED_DECODER_RECEIVE_ORDER", reason=None,
                recorded_frames=len(rows), expected_frames=stream.frames,
                longest_receive_gap_ms=round(max(gaps), 3) if gaps else None,
                non_gi_backward_finding_after_gap=strongest)

def independent_triage(left: dict, right: dict) -> dict:
    """Evidence for *separate* sessions, never a paired BRCB reliability comparison."""
    _require_matching_authority(left, right)
    la = {k: s for k, s in left["streams"].items() if s.kind == "BRCB"}
    ra = {k: s for k, s in right["streams"].items() if s.kind == "BRCB"}
    if not la or not ra:
        raise ValueError("no BRCB streams")
    matched = set(la) == set(ra) and all(
        la[key].dataset.casefold() == ra[key].dataset.casefold() for key in la)
    def session_rows(streams: dict[str, Stream]) -> list[dict]:
        rows = []
        for stream in sorted(streams.values(), key=lambda s: s.rcb.casefold()):
            gi = stream.sq_with_gi_reason
            if stream.anomalous_frames > stream.frames or (
                gi is not None and gi > stream.discontinuities):
                raise ValueError(f"impossible BRCB diagnostic counts for {stream.rcb}")
            rows.append(dict(rcb=stream.rcb, dataset=stream.dataset,
                             received_frames=stream.frames, bufovfl_true_frames=stream.overflow_frames,
                             anomalous_frames=stream.anomalous_frames,
                             sq_discontinuities=stream.discontinuities,
                             sq_with_gi_reason=gi,
                             sq_without_gi_reason=(stream.discontinuities - gi) if gi is not None else None,
                             received_after_last_finding=stream.after_last_finding,
                             gi_reason_frames=stream.gi_reason_frames,
                             last_anomaly_ms_after_start_return=_elapsed_ms(
                                 stream.start_return_utc, stream.last_anomaly_utc),
                             retained_chronology_frames=len(stream.chronology),
                             retained_anomaly_frames=len(stream.recent_anomalies),
                             receive_order_gap_evidence=_receive_order_gap_evidence(stream)))
        return rows
    return dict(status="INDEPENDENT_SESSIONS_OBSERVATION_ONLY",
                same_slot_dataset_scope=matched,
                paired_comparison_blocked_reason=None if matched else "different BRCB slots or DataSets",
                ied=left["ied"], fingerprint=left["fingerprint"], engine_commit=left["engine"],
                sessions=dict(open_scl=session_rows(la), discovery=session_rows(ra)),
                qualification="NOT_PAIRED_RELIABILITY_COMPARISON; DECODER_RECEIVE_TIMESTAMPS_NOT_WIRE_ORDER; NOT_SOE_LOSSLESS; NOT_R10_PHYSICAL_ACCEPTANCE")

def compare(left: dict, right: dict) -> dict:
    _require_matching_authority(left, right)
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
                            left_bufovfl_frames=a.overflow_frames,
                            right_bufovfl_frames=b.overflow_frames,
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
    ap.add_argument("--independent-triage", action="store_true",
                    help="separate per-session evidence when real BRCB slots differ; never paired reliability")
    args = ap.parse_args()
    try:
        a = parse_report(args.open_scl.read_text(encoding="utf-8-sig"))
        b = parse_report(args.discovery.read_text(encoding="utf-8-sig"))
        result = independent_triage(a, b) if args.independent_triage else compare(a, b)
    except (OSError, UnicodeError, ValueError) as exc:
        ap.exit(2, f"INCOMPARABLE: {exc}\n")
    formatted = json.dumps(result, indent=2, sort_keys=True)
    if args.output:
        args.output.write_text(formatted + "\n", encoding="utf-8")
    print(formatted)
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
