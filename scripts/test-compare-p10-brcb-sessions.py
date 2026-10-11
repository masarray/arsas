#!/usr/bin/env python3
"""Regress fail-closed cross-session BRCB evidence comparisons."""
import importlib.util
import unittest
from pathlib import Path

PATH = Path(__file__).with_name("compare-p10-brcb-sessions.py")
spec = importlib.util.spec_from_file_location("p108d", PATH)
mod = importlib.util.module_from_spec(spec)
import sys
sys.modules["p108d"] = mod
spec.loader.exec_module(mod)

FP = "439d4dc0507019533b154ed68f7f4007f39f6be92b5cd3f413ff3b84f619727e"
ENGINE = "00ad2819b99ffe6b3f885e59aa24a6afb2b4d8e1"

def report(ied="BCUGE", fp=FP, engine=ENGINE, slot="GW01",
           dataset="BCUGEF650/LLN0.CSWI", frames=25, overflow=1, with_chronology=True):
    return f"""ARSAS Diagnostic Report
Engine version  : 1.6.40+{engine}
  IED              : {ied}
    Discovery      : LiveDiscovery: fingerprint={fp}, requested=67
  stream[0]      : BRCB, rcb=BCUGEF650/LLN0.BR.{slot}, dataset={dataset}, rptId=BRB01, frames={frames}, SqNum=25, findings=2, BufOvfl={overflow}
    RCB triage    : anomalousFrames=1, sqDiscontinuities=1, sqChangedEntryID=1
    RCB follow-up : receivedAfterLastFinding=3, firstFrameUtc=2026-10-11T06:25:51Z
    BRCB chronology: first=2, recent=0, omittedMiddle=0
{("      frame#1: receivedUtc=unknown, SqNum=4, GIreason=NO, BufOvfl=NO, EntryIDrelation=unverified, findings=0" + chr(10)) if with_chronology else ""}    OptFlds       : decoded=25/25
  stream[1]      : URCB, rcb=BCUGEF650/LLN0.RP.URB01, dataset=MMXU, rptId=URB01, frames=1, SqNum=1, findings=0
"""

class TestComparator(unittest.TestCase):
    def test_exact_same_slot_only_maintains_not_soe_qualified(self):
        result = mod.compare(mod.parse_report(report()), mod.parse_report(report(frames=43)))
        self.assertEqual("SAME_SLOT_OBSERVATION_ONLY", result["status"])
        self.assertEqual("NOT_WIRE_ORDER_PROOF; NOT_SOE_LOSSLESS; NOT_R10_PHYSICAL_ACCEPTANCE",
                         result["qualification"])
        self.assertEqual(25, result["slots"][0]["left_frames"])
        self.assertEqual(43, result["slots"][0]["right_frames"])
        self.assertEqual(1, result["slots"][0]["left_chronology_frames"])

    def test_different_slots_do_not_allow_false_comparison(self):
        with self.assertRaisesRegex(ValueError, "different BRCB slots"):
            mod.compare(mod.parse_report(report(slot="GW01")),
                        mod.parse_report(report(slot="SOGI101")))

    def test_different_semantics_fail_closed(self):
        with self.assertRaisesRegex(ValueError, "fingerprints"):
            mod.compare(mod.parse_report(report()),
                        mod.parse_report(report(fp="f" * 64)))

    def test_engine_mismatch_fail_closed(self):
        with self.assertRaisesRegex(ValueError, "engine commits"):
            mod.compare(mod.parse_report(report()),
                        mod.parse_report(report(engine="f" * 40)))

    def test_ied_mismatch_fail_closed(self):
        with self.assertRaisesRegex(ValueError, "IED identities"):
            mod.compare(mod.parse_report(report()),
                        mod.parse_report(report(ied="WRONG")))

    def test_different_datasets_fail_closed(self):
        with self.assertRaisesRegex(ValueError, "different DataSet"):
            mod.compare(mod.parse_report(report()),
                        mod.parse_report(report(dataset="BCUGEF650/LLN0.EVEGGIO")))

    def test_missing_provenance_rejected(self):
        with self.assertRaisesRegex(ValueError, "missing IED"):
            mod.parse_report("stream[0] : BRCB, rcb=a, dataset=b, frames=1")

    def test_duplicate_stream_rejected(self):
        data = report()
        line = next(l for l in data.splitlines() if "stream[0]" in l)
        with self.assertRaisesRegex(ValueError, "duplicate RCB"):
            mod.parse_report(data + line.replace("stream[0]", "stream[9]") + "\n")

    def test_same_slot_overflow_counts_are_observable_not_loss_estimates(self):
        result = mod.compare(mod.parse_report(report(overflow=1)),
                             mod.parse_report(report(frames=43, overflow=3)))
        slot = result["slots"][0]
        self.assertEqual(1, slot["left_bufovfl_frames"])
        self.assertEqual(3, slot["right_bufovfl_frames"])
        self.assertIn("NOT_SOE_LOSSLESS", result["qualification"])

    def test_brcb_missing_overflow_evidence_fails_closed(self):
        truncated = report().replace(", BufOvfl=1", "")
        with self.assertRaisesRegex(ValueError, "missing BRCB BufOvfl count"):
            mod.parse_report(truncated)

    def test_brcb_overflow_count_cannot_exceed_decoded_frames(self):
        with self.assertRaisesRegex(ValueError, "exceeds received frames"):
            mod.parse_report(report(frames=2, overflow=3))

    def test_clean_brcb_zero_overflow_remains_comparable(self):
        result = mod.compare(mod.parse_report(report(overflow=0)),
                             mod.parse_report(report(overflow=0)))
        self.assertEqual(0, result["slots"][0]["left_bufovfl_frames"])
        self.assertEqual(0, result["slots"][0]["right_bufovfl_frames"])

    def test_empty_chronology_allowed_but_does_not_invent_events(self):
        result = mod.compare(mod.parse_report(report(with_chronology=False)),
                             mod.parse_report(report(with_chronology=False)))
        self.assertEqual(0, result["slots"][0]["left_chronology_frames"])
        self.assertIn("NOT_WIRE_ORDER_PROOF", result["qualification"])

if __name__ == "__main__":
    unittest.main()
