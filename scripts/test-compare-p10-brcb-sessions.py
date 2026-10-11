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
    RCB triage    : anomalousFrames=1, sqDiscontinuities=1, sqChangedEntryID=1, sqWithGIreason=0
    RCB follow-up : receivedAfterLastFinding=3, firstFrameUtc=2026-10-11T06:25:51Z
    RCB lifecycle : RptEnaWrite=YES, GIWrite=YES, startReturnUtc=2026-10-11T06:25:50Z, GIreasonFrames=1, lastAnomalyUtc=2026-10-11T06:25:51Z
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

    def test_independent_triage_different_real_slots_is_explicitly_unpaired(self):
        data = mod.independent_triage(mod.parse_report(report(slot="GW01")),
                                      mod.parse_report(report(slot="SOGI101", frames=43, overflow=3)))
        self.assertEqual("INDEPENDENT_SESSIONS_OBSERVATION_ONLY", data["status"])
        self.assertFalse(data["same_slot_dataset_scope"])
        self.assertEqual("different BRCB slots or DataSets", data["paired_comparison_blocked_reason"])
        self.assertNotIn("slots", data)
        self.assertEqual(1, data["sessions"]["open_scl"][0]["bufovfl_true_frames"])
        self.assertEqual(3, data["sessions"]["discovery"][0]["bufovfl_true_frames"])
        self.assertIn("NOT_PAIRED_RELIABILITY_COMPARISON", data["qualification"])

    def test_independent_triage_same_slots_does_not_establish_losslessness(self):
        data = mod.independent_triage(mod.parse_report(report()), mod.parse_report(report()))
        self.assertTrue(data["same_slot_dataset_scope"])
        self.assertIn("NOT_SOE_LOSSLESS", data["qualification"])

    def test_gi_context_and_monotonic_startup_delay_are_observational(self):
        one = mod.independent_triage(mod.parse_report(report()), mod.parse_report(report()))
        stream = one["sessions"]["open_scl"][0]
        self.assertEqual(1, stream["sq_without_gi_reason"])
        self.assertEqual(1, stream["gi_reason_frames"])
        self.assertEqual(1000.0, stream["last_anomaly_ms_after_start_return"])
        self.assertEqual(3, stream["received_after_last_finding"])

    def test_missing_gi_and_lifecycle_are_unknown_not_zero(self):
        data = report().replace(", sqWithGIreason=0", "")
        data = "\n".join(x for x in data.splitlines() if "RCB lifecycle" not in x)
        row = mod.independent_triage(mod.parse_report(data), mod.parse_report(data))["sessions"]["open_scl"][0]
        self.assertIsNone(row["sq_with_gi_reason"])
        self.assertIsNone(row["sq_without_gi_reason"])
        self.assertIsNone(row["last_anomaly_ms_after_start_return"])

    def test_impossible_gi_discontinuity_counts_are_rejected(self):
        data = report().replace("sqWithGIreason=0", "sqWithGIreason=2")
        with self.assertRaisesRegex(ValueError, "impossible BRCB diagnostic counts"):
            mod.independent_triage(mod.parse_report(data), mod.parse_report(report()))

    def test_independent_triage_mismatched_engine_still_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "different engine commits"):
            mod.independent_triage(mod.parse_report(report()),
                                   mod.parse_report(report(engine="f" * 40)))

    def _full_chronology_report(self):
        data = report(frames=4, overflow=1, with_chronology=False)
        data = data.replace("BRCB chronology: first=2, recent=0, omittedMiddle=0",
                            "BRCB chronology: first=4, recent=0, omittedMiddle=0")
        data = data.replace("    OptFlds       : decoded=25/25", """      frame#1: receivedUtc=2026-10-11T06:25:51Z, SqNum=10, GIreason=NO, BufOvfl=YES, EntryIDrelation=unverified, findings=1
      frame#2: receivedUtc=2026-10-11T06:25:51.100Z, SqNum=11, GIreason=NO, BufOvfl=NO, EntryIDrelation=changed, findings=0
      frame#3: receivedUtc=2026-10-11T06:26:11Z, SqNum=1, GIreason=NO, BufOvfl=NO, EntryIDrelation=changed, findings=1
      frame#4: receivedUtc=2026-10-11T06:26:17Z, SqNum=2, GIreason=NO, BufOvfl=NO, EntryIDrelation=changed, findings=0
    OptFlds       : decoded=25/25""")
        return data

    def test_late_non_gi_recurrence_after_full_receive_gap(self):
        sample = self._full_chronology_report()
        triage = mod.independent_triage(mod.parse_report(sample), mod.parse_report(sample))
        evidence = triage["sessions"]["open_scl"][0]["receive_order_gap_evidence"]
        self.assertEqual("COMPLETE_BOUNDED_DECODER_RECEIVE_ORDER", evidence["coverage"])
        self.assertEqual(19900.0, evidence["longest_receive_gap_ms"])
        anomaly = evidence["non_gi_backward_finding_after_gap"]
        self.assertEqual(3, anomaly["frame"])
        self.assertEqual((11, 1), (anomaly["previous_sqnum"], anomaly["sqnum"]))
        self.assertEqual(19900.0, anomaly["gap_ms"])
        self.assertEqual(21000.0, anomaly["elapsed_since_start_return_ms"])
        self.assertIn("NOT_SOE_LOSSLESS", triage["qualification"])

    def test_truncated_chronology_never_invents_receive_gap(self):
        sample = self._full_chronology_report()
        sample = "\n".join(line for line in sample.splitlines() if "frame#2:" not in line)
        evidence = mod.independent_triage(mod.parse_report(sample), mod.parse_report(sample))["sessions"]["open_scl"][0]["receive_order_gap_evidence"]
        self.assertEqual("UNVERIFIABLE", evidence["coverage"])
        self.assertEqual("RETAINED_CHRONOLOGY_INCOMPLETE", evidence["reason"])
        self.assertIsNone(evidence["longest_receive_gap_ms"])
        self.assertIsNone(evidence["non_gi_backward_finding_after_gap"])

    def test_omitted_middle_never_certifies_gaps(self):
        sample = self._full_chronology_report()
        sample = sample.replace("omittedMiddle=0", "omittedMiddle=1")
        evidence = mod.independent_triage(mod.parse_report(sample), mod.parse_report(sample))["sessions"]["open_scl"][0]["receive_order_gap_evidence"]
        self.assertEqual("CHRONOLOGY_HAS_OMITTED_OR_UNACCOUNTED_FRAMES", evidence["reason"])

    def test_out_of_order_or_duplicate_frame_ids_do_not_certify_receive_order(self):
        sample = self._full_chronology_report()
        sample = sample.replace("frame#3:", "frame#2:")
        evidence = mod.independent_triage(mod.parse_report(sample), mod.parse_report(sample))["sessions"]["open_scl"][0]["receive_order_gap_evidence"]
        self.assertEqual("RECEIVE_FRAME_INDICES_NOT_CONTIGUOUS", evidence["reason"])

    def test_nonmonotonic_timestamps_do_not_certify_receive_order(self):
        sample = self._full_chronology_report()
        sample = sample.replace("frame#3: receivedUtc=2026-10-11T06:26:11Z",
                                "frame#3: receivedUtc=2026-10-11T06:25:50Z")
        evidence = mod.independent_triage(mod.parse_report(sample), mod.parse_report(sample))["sessions"]["open_scl"][0]["receive_order_gap_evidence"]
        self.assertEqual("NONMONOTONIC_DECODER_RECEIVE_TIMESTAMPS", evidence["reason"])

    def test_gi_reason_only_and_unverified_fixture_do_not_claim_non_gi_recurrence(self):
        sample = self._full_chronology_report()
        sample = sample.replace("frame#3: receivedUtc=2026-10-11T06:26:11Z, SqNum=1, GIreason=NO",
                                "frame#3: receivedUtc=2026-10-11T06:26:11Z, SqNum=1, GIreason=YES")
        evidence = mod.independent_triage(mod.parse_report(sample), mod.parse_report(sample))["sessions"]["open_scl"][0]["receive_order_gap_evidence"]
        self.assertEqual("COMPLETE_BOUNDED_DECODER_RECEIVE_ORDER", evidence["coverage"])
        self.assertIsNone(evidence["non_gi_backward_finding_after_gap"])
        old = mod.independent_triage(mod.parse_report(report()), mod.parse_report(report()))
        self.assertEqual("UNVERIFIABLE", old["sessions"]["open_scl"][0]["receive_order_gap_evidence"]["coverage"])

    def test_empty_chronology_allowed_but_does_not_invent_events(self):
        result = mod.compare(mod.parse_report(report(with_chronology=False)),
                             mod.parse_report(report(with_chronology=False)))
        self.assertEqual(0, result["slots"][0]["left_chronology_frames"])
        self.assertIn("NOT_WIRE_ORDER_PROOF", result["qualification"])

if __name__ == "__main__":
    unittest.main()
