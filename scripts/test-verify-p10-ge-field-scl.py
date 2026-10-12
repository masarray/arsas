#!/usr/bin/env python3
"""Offline P10.4 GE field SCL gate regression: no hardware or network required."""
import hashlib
import importlib.util
import tempfile
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

SCRIPT = Path(__file__).with_name("verify-p10-ge-field-scl.py")
spec = importlib.util.spec_from_file_location("p10_ge_gate", SCRIPT)
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
N = gate.NS
T = gate.TAG


def example(*, ip="192.16.1.33", ied="BCUGE", inst="F650",
            ap="S1", wire_ld_name=None, members=124, remove_ln=False):
    ET.register_namespace("", N)
    root = ET.Element(T("SCL"))
    comm = ET.SubElement(root, T("Communication"))
    subnet = ET.SubElement(comm, T("SubNetwork"))
    conn = ET.SubElement(subnet, T("ConnectedAP"),
                         {"iedName": ied, "apName": ap})
    address = ET.SubElement(conn, T("Address"))
    ET.SubElement(address, T("P"), {"type": "IP"}).text = ip
    node = ET.SubElement(root, T("IED"), {"name": ied})
    access = ET.SubElement(node, T("AccessPoint"), {"name": ap})
    server = ET.SubElement(access, T("Server"))
    attrs = {"inst": inst}
    if wire_ld_name:
        attrs["ldName"] = wire_ld_name
    ld = ET.SubElement(server, T("LDevice"), attrs)
    ln = ET.SubElement(ld, T("LN0"), {"lnClass": "LLN0"})
    if not remove_ln:
        ET.SubElement(ld, T("LN"), {"lnClass": "CSWI", "inst": "7"})
    for index in range(8):
        ds = ET.SubElement(ln, T("DataSet"), {"name": f"DS{index + 1}"})
        # 4*16+4*15=124: non-overlapping ordered FCDA evidence.
        count = (16 if index < 4 else 15)
        if members != 124 and index == 0:
            count += members - 124
        for subindex in range(max(count, 0)):
            ET.SubElement(ds, T("FCDA"),
                          {"ldInst": inst, "lnClass": "CSWI",
                           "lnInst": "7", "doName": f"Pos{subindex}",
                           "daName": "stVal", "fc": "ST"})
        if index < 6:
            ET.SubElement(ln, T("ReportControl"),
                          {"name": f"RCB{index + 1}", "datSet": f"DS{index + 1}",
                           "buffered": "true"})
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


class GeFieldEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.src = Path(self.temp.name, "BCU_GE.iid")
        self.out = Path(self.temp.name, "BCUGE-discovery.icd")
        self.src.write_bytes(example())
        self.out.write_bytes(example())

    def check(self, sha=None, domain="BCUGEF650"):
        return gate.assess(
            self.src, self.out,
            sha or hashlib.sha256(self.src.read_bytes()).hexdigest(),
            domain, "192.16.1.33", "S1")

    def test_valid_structure_is_explicitly_not_physical_qualification(self):
        result = self.check()
        self.assertEqual("STRUCTURE_PASS_ONLY", result["structuralVerdict"])
        self.assertEqual(8, result["dataSets"])
        self.assertEqual(124, result["staticMembers"])
        self.assertEqual("NOT_VERIFIED", result["physicalQualification"])
        self.assertFalse(result["r10PromotionAllowed"])
        self.assertTrue(any("BRCB" in s for s in result["unverifiedPhysicalGates"]))

    def test_source_sha_drift_fails_closed(self):
        with self.assertRaisesRegex(gate.RejectedEvidence, "SHA-256 mismatch"):
            self.check("0" * 64)

    def test_same_scl_file_fails_closed(self):
        with self.assertRaisesRegex(gate.RejectedEvidence, "same file"):
            gate.assess(self.src, self.src,
                        hashlib.sha256(self.src.read_bytes()).hexdigest(),
                        "BCUGEF650", "192.16.1.33", "S1")

    def test_wrong_observed_wire_domain_fails_even_if_scl_graph_equal(self):
        self.assertEqual("FAIL", self.check(domain="BCUGECTRL")["structuralVerdict"])

    def test_wrong_endpoint_rejected(self):
        self.out.write_bytes(example(ip="192.16.1.34"))
        self.assertEqual("FAIL", self.check()["structuralVerdict"])

    def test_ipless_scl_is_explicit_review_not_invented_endpoint(self):
        root = ET.fromstring(example())
        root.find(f".//{T('Address')}").clear()
        self.out.write_bytes(ET.tostring(root))
        result = self.check()
        self.assertEqual("STRUCTURE_PASS_ONLY", result["structuralVerdict"])
        self.assertTrue(any("no authoritative ConnectedAP IP" in s for s in result["review"]))

    def test_f650_must_not_become_mms_domain_inst(self):
        self.out.write_bytes(example(inst="BCUGEF650"))
        self.assertEqual("FAIL", self.check()["structuralVerdict"])

    def test_extra_explicit_ldname_is_detected(self):
        self.out.write_bytes(example(wire_ld_name="OTHER_WIRE_DOMAIN"))
        self.assertEqual("FAIL", self.check()["structuralVerdict"])

    def test_missing_static_member_fails(self):
        self.out.write_bytes(example(members=123))
        self.assertEqual("FAIL", self.check()["structuralVerdict"])

    def test_member_order_change_fails_not_just_count(self):
        root = ET.fromstring(example())
        ds = root.find(f".//{T('DataSet')}")
        ds[0], ds[1] = ds[1], ds[0]
        self.out.write_bytes(ET.tostring(root))
        self.assertEqual("FAIL", self.check()["structuralVerdict"])

    def test_fcda_unknown_ln_rejected(self):
        self.out.write_bytes(example(remove_ln=True))
        with self.assertRaisesRegex(gate.RejectedEvidence, "unknown LN"):
            self.check()

    def test_no_false_rcb_continuity_from_matching_logical_rcbs(self):
        result = self.check()
        self.assertEqual(6, result["logicalReportControls"]["discoveryExport"])
        self.assertEqual("NOT_VERIFIED", result["physicalQualification"])

    def test_different_rcb_configuration_is_review_not_physical_pass(self):
        root = ET.fromstring(example())
        root.find(f".//{T('ReportControl')}").set("buffered", "false")
        self.out.write_bytes(ET.tostring(root))
        result = self.check()
        self.assertEqual("STRUCTURE_PASS_ONLY", result["structuralVerdict"])
        self.assertTrue(any("Logical ReportControl" in s for s in result["review"]))
        self.assertFalse(result["r10PromotionAllowed"])

    def test_source_extra_design_ln_is_review_not_dataset_failure(self):
        root = ET.fromstring(example())
        ld = root.find(f".//{T('LDevice')}")
        ET.SubElement(ld, T("LN"), {"lnClass": "MMXU", "inst": "1"})
        self.src.write_bytes(ET.tostring(root))
        result = self.check()
        self.assertEqual("STRUCTURE_PASS_ONLY", result["structuralVerdict"])
        self.assertTrue(any("LN design/live scope differs" in s for s in result["review"]))

    def test_external_ied_fcda_is_not_silently_treated_as_local(self):
        root = ET.fromstring(example())
        root.find(f".//{T('FCDA')}").set("iedName", "DIFFERENT_IED")
        self.out.write_bytes(ET.tostring(root))
        with self.assertRaisesRegex(gate.RejectedEvidence, "external IED"):
            self.check()

    def test_dtd_and_entities_rejected(self):
        evil = b'<!DOCTYPE SCL [<!ENTITY x "abc">]><SCL xmlns="' + N.encode() + b'"/>'
        self.out.write_bytes(evil)
        with self.assertRaisesRegex(gate.RejectedEvidence, "DTD/ENTITY"):
            self.check()

    def test_ambiguous_accesspoint_rejected(self):
        root = ET.fromstring(example())
        access = root.find(f".//{T('AccessPoint')}")
        root.find(f".//{T('IED')}").append(ET.fromstring(ET.tostring(access)))
        self.out.write_bytes(ET.tostring(root))
        with self.assertRaisesRegex(gate.RejectedEvidence, "AccessPoint"):
            self.check()


if __name__ == "__main__":
    unittest.main()
