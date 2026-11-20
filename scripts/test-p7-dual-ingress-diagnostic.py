#!/usr/bin/env python3
"""Sanitized P7.6 fail-closed qualification fixtures; no device/network access."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("verify-p7-dual-ingress-diagnostic.py")
spec = importlib.util.spec_from_file_location("p7_qualifier", SCRIPT)
assert spec and spec.loader
qualifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(qualifier)
SHA = "a" * 64
STAMP = "2026-10-08T10:01:02.0000000+00:00"


def ingress(label: str, runtime: str, *, fingerprint: str = SHA,
            brcb: int = 6, urcb: int = 6,
            planned: int = 2, routed: int = 2,
            first: str = STAMP, brcb_bindings: int = 6, urcb_bindings: int = 6,
            last_status: str = "ROUTED") -> str:
    return (
        f"  {label}      : {runtime}: fingerprint={fingerprint}, requested=12, "
        f"plans={planned}, staticBRCB={brcb}, staticURCB={urcb}, uncovered=0\n"
        f"    traffic       : {routed}/{planned} exact static RCB target(s) routed; first={first}\n"
        f"    runtime       : static-brcb|dataset=ied/lln0.events|rcb=ied/lln0.br.rpt_ind01|bindings={brcb_bindings} [ROUTED]\n"
        f"    runtime       : static-urcb|dataset=ied/lln0.analog|rcb=ied/lln0.rp.rpt_meas01|bindings={urcb_bindings} [{last_status}]\n"
    )


def diagnostic(*, discovery: str | None = None,
               scl: str | None = None, traffic: bool = True, ap: str = "F") -> str:
    routed = "DUAL INGRESS TRAFFIC PROVEN (routed static RCB data from both paths)" if traffic else "TRAFFIC PENDING"
    return (
        "ARSAS Diagnostic Report\n"
        "IED              : GR_X_7SX85\n"
        "Endpoint         : 198.51.100.12:102\n"
        f"SCD selected AP  : {ap}\n"
        f"Static parity    : MATCH • {SHA}\n"
        f"Routed traffic   : {routed}\n"
        + (discovery if discovery is not None else ingress("Discovery", "LiveDiscovery"))
        + (scl if scl is not None else ingress("Open SCL ", "OpenScl"))
    )


class QualificationTests(unittest.TestCase):
    def assertNotQualified(self, text: str, message: str, **kwargs) -> None:
        with self.assertRaisesRegex(qualifier.QualificationError, message):
            qualifier.qualify(text, ied="GR_X_7SX85", **kwargs)

    def test_exact_two_ingresses_both_brcb_urcb_with_source_identity(self):
        result = qualifier.qualify(diagnostic(), ied="GR_X_7SX85",
                                   min_brcb=6, min_urcb=6, selected_ap="F")
        self.assertFalse(result["physicalVerification"])
        self.assertFalse(result["wirePcapVerification"])
        self.assertIn("NOT_PHYSICAL_VERIFIED", result["qualification"])
        self.assertEqual(result["discovery"]["plannedTargets"], 2)
        self.assertEqual(result["openScl"]["routedTargets"], 2)

    def test_semantic_match_alone_does_not_qualify(self):
        self.assertNotQualified(diagnostic(traffic=False), "dual-ingress routed traffic proof")

    def test_single_ingress_does_not_qualify(self):
        self.assertNotQualified(diagnostic(scl=""), "Both independent ingress")

    def test_pending_target_does_not_qualify(self):
        self.assertNotQualified(
            diagnostic(scl=ingress("Open SCL ", "OpenScl", last_status="PENDING")),
            "unrouted")

    def test_forged_count_or_truncated_inventory_does_not_qualify(self):
        self.assertNotQualified(
            diagnostic(scl=ingress("Open SCL ", "OpenScl", planned=9, routed=9)),
            "incomplete/truncated")

    def test_brcb_only_is_not_urcb_proof(self):
        self.assertNotQualified(diagnostic(scl=ingress("Open SCL ", "OpenScl")
                           .replace("static-urcb|", "static-brcb|")
                           .replace("rpt_meas01", "rpt_ind02")), "both BRCB and URCB")

    def test_mismatched_semantic_sha_is_rejected(self):
        self.assertNotQualified(
            diagnostic(scl=ingress("Open SCL ", "OpenScl", fingerprint="b" * 64)),
            "fingerprint does not match")

    def test_wrong_ied_is_rejected(self):
        with self.assertRaisesRegex(qualifier.QualificationError, "Exact IED identity"):
            qualifier.qualify(diagnostic(), ied="DIFFERENT")

    def test_two_sections_cannot_borrow_traffic_from_another_ied(self):
        text = (diagnostic(scl="") +
                "IED              : OTHER_IED\n" +
                ingress("Open SCL ", "OpenScl"))
        self.assertNotQualified(text, "Both independent ingress")

    def test_duplicate_ied_block_is_rejected(self):
        self.assertNotQualified(diagnostic() + diagnostic(), "IED identity")

    def test_selected_ap_must_be_source_exact(self):
        self.assertNotQualified(diagnostic(), "AccessPoint", selected_ap="J")

    def test_missing_timestamp_is_not_traffic_proof(self):
        self.assertNotQualified(
            diagnostic(scl=ingress("Open SCL ", "OpenScl", first="pending")),
            "timestamp")

    def test_duplicate_target_is_rejected(self):
        target = "static-urcb|dataset=ied/lln0.analog|rcb=ied/lln0.rp.rpt_meas01"
        dup = ingress("Open SCL ", "OpenScl").replace(target,
            "static-brcb|dataset=ied/lln0.events|rcb=ied/lln0.br.rpt_ind01")
        self.assertNotQualified(diagnostic(scl=dup), "duplicate RCB target")

    def test_incomplete_or_emergency_report_not_accepted(self):
        self.assertNotQualified(
            diagnostic().replace("ARSAS Diagnostic Report", "ARSAS Emergency Diagnostic Report"),
            "Full Copy Diagnostic")

    def test_explicit_6_signal_threshold_is_not_downgraded(self):
        self.assertNotQualified(diagnostic(scl=ingress("Open SCL ", "OpenScl", brcb=5)),
                                "insufficient static", min_brcb=6, min_urcb=6)

    def test_missing_full_count_even_if_summary_says_proven(self):
        self.assertNotQualified(
            diagnostic(discovery=ingress("Discovery", "LiveDiscovery").split(
                "    runtime       : static-urcb|")[0]),
            "incomplete/truncated")


    def test_selected_ap_required_and_proven_even_when_optional_arg_omitted(self):
        verdict = qualifier.qualify(diagnostic(), ied="GR_X_7SX85")
        self.assertEqual("F", verdict["selectedAccessPoint"])
        self.assertNotQualified(diagnostic().replace("SCD selected AP  : F\n", ""),
                                "selected AccessPoint")
        self.assertNotQualified(diagnostic().replace("SCD selected AP  : F",
                    "SCD selected AP  : pending"), "not a proven selected AP")
        self.assertNotQualified(diagnostic().replace("SCD selected AP  : F",
                    "SCD selected AP  : F\nSCD selected AP  : J"), "exactly one")

    def test_each_ingress_requires_exact_per_family_binding_incidence(self):
        # Summary counters cannot substitute for actual target totals.
        for ingress_name, label, source in (
            ("discovery", "Discovery", "LiveDiscovery"),
            ("scl", "Open SCL ", "OpenScl"),
        ):
            for parameter in ("brcb_bindings", "urcb_bindings"):
                with self.subTest(ingress=ingress_name, family=parameter):
                    changed = ingress(label, source, **{parameter: 1})
                    self.assertNotQualified(diagnostic(**{ingress_name: changed}),
                                            "target bindings do not match")

    def test_binding_incidence_can_exceed_unique_requested_signal_count(self):
        # Reused points across RCBs count as multiple binding incidences.
        result = qualifier.qualify(diagnostic().replace("requested=12", "requested=9"),
                                   ied="GR_X_7SX85", min_brcb=6, min_urcb=6)
        self.assertEqual(6, result["openScl"]["brcbSignals"])
        self.assertEqual(6, result["discovery"]["urcbSignals"])

    def test_overlong_numeric_fields_and_unicode_digits_fail_closed(self):
        many = "9" * 5000
        cases = (
            ("ingress", diagnostic().replace("requested=12", "requested=" + many, 1),
             "Malformed ingress header"),
            ("traffic", diagnostic().replace("traffic       : 2/2",
                                            "traffic       : " + many + "/2", 1),
             "Unparseable target or traffic"),
            ("binding", diagnostic().replace("bindings=6 [ROUTED]",
                                            "bindings=" + many + " [ROUTED]", 1),
             "Unparseable target or traffic"),
            ("unicode", diagnostic().replace("requested=12", "requested=１２", 1),
             "Malformed ingress header"),
        )
        for name, bad, error in cases:
            with self.subTest(field=name):
                self.assertNotQualified(bad, error)

    def test_cli_long_numeric_recheck_cannot_leave_stale_pass(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            incoming, result = root / "private.txt", root / "verdict.json"
            cmd = [sys.executable, str(SCRIPT), "--diagnostic", str(incoming),
                   "--ied", "GR_X_7SX85", "--output", str(result)]
            incoming.write_text(diagnostic(), encoding="utf-8")
            self.assertEqual(0, subprocess.run(cmd, capture_output=True).returncode)
            self.assertIn("PASS", json.loads(result.read_text())["qualification"])
            incoming.write_text(
                diagnostic().replace("requested=12", "requested=" + "9" * 5000, 1),
                encoding="utf-8")
            rejected = subprocess.run(cmd, capture_output=True, text=True)
            self.assertNotEqual(0, rejected.returncode)
            self.assertNotIn("Traceback", rejected.stderr)
            saved = json.loads(result.read_text())
            self.assertEqual("NOT_ESTABLISHED", saved["qualification"])
            self.assertFalse(saved["physicalVerification"])
            self.assertFalse(saved["wirePcapVerification"])
            self.assertFalse(list(root.glob(".verdict.json.*.tmp")))

    def test_large_or_nul_diagnostic_fail_closed(self):
        self.assertNotQualified("ARSAS Diagnostic Report\n" +
                "X" * (qualifier.MAX_DIAGNOSTIC_BYTES + 1), "exceeds safe size")
        self.assertNotQualified(diagnostic() + "\x00", "contains NUL")

    def test_cli_failed_recheck_replaces_old_positive_verdict(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            incoming, result = root / "private.txt", root / "result.json"
            cmd = [sys.executable, str(SCRIPT), "--diagnostic", str(incoming),
                   "--ied", "GR_X_7SX85", "--selected-ap", "F",
                   "--min-brcb-signals", "6", "--min-urcb-signals", "6",
                   "--output", str(result)]
            incoming.write_text(diagnostic(), encoding="utf-8")
            self.assertEqual(0, subprocess.run(cmd, capture_output=True).returncode)
            self.assertEqual("DIAGNOSTIC_CONSISTENCY_PASS_NOT_PHYSICAL_VERIFIED",
                json.loads(result.read_text())["qualification"])
            incoming.write_text(diagnostic(traffic=False), encoding="utf-8")
            self.assertNotEqual(0, subprocess.run(cmd, capture_output=True).returncode)
            failed = json.loads(result.read_text())
            self.assertEqual("NOT_ESTABLISHED", failed["qualification"])
            self.assertFalse(failed["physicalVerification"])
            self.assertFalse(failed["wirePcapVerification"])
            self.assertFalse(list(root.glob(".result.json.*.tmp")))

    def test_bounded_file_and_result_symlink_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            f = root / "huge.txt"
            f.write_bytes(b"Z" * (qualifier.MAX_DIAGNOSTIC_BYTES + 1))
            with self.assertRaisesRegex(qualifier.QualificationError, "exceeds safe size"):
                qualifier.read_bounded_diagnostic(f)
            destination, link = root / "other.json", root / "symlink.json"
            destination.write_text('{"qualification":"KEEP"}')
            try:
                link.symlink_to(destination)
            except (OSError, NotImplementedError):
                self.skipTest("Symlinks unavailable")
            with self.assertRaisesRegex(OSError, "Refusing symlink"):
                qualifier.write_result(link, {"qualification": "PASS"})
            self.assertEqual('{"qualification":"KEEP"}', destination.read_text())

if __name__ == "__main__":
    unittest.main()
