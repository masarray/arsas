#!/usr/bin/env python3
"""Offline CI-P3J-A native CTest evidence fail-closed fixtures."""
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SRC = Path(__file__).with_name("write-ci-native-ctest-proof.py")
spec = importlib.util.spec_from_file_location("native_ctest_proof", SRC)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

GOOD_JUNIT = """<?xml version="1.0" encoding="UTF-8"?>
<testsuite name="ArdIrec" tests="2" failures="0" errors="0" skipped="0">
  <testcase name="BridgeAbi" classname="native" time="0.1" />
  <testcase name="LocusGeometry" classname="native" time="0.2" />
</testsuite>
"""

class NativeCTestProofTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.junit = self.root / "ctest.xml"
        self.bridge = self.root / "ardirec_bridge.dll"
        self.output = self.root / "ci-native-ctest-authority.json"
        self.junit.write_text(GOOD_JUNIT, encoding="utf-8")
        self.bridge.write_bytes(b"pinned-native-test-bridge")
        self.args = dict(
            junit=self.junit, bridge=self.bridge, source_sha="a" * 40,
            engine_sha="b" * 40, ardirec_sha="c" * 40, run_id=123,
            attempt=1, event="pull_request",
        )

    def tearDown(self):
        self.tmp.cleanup()

    def seal(self):
        return module.write_or_verify(self.output, verify_only=False, **self.args)

    def verify(self):
        return module.write_or_verify(self.output, verify_only=True, **self.args)

    def test_exact_native_proof_passes_and_is_nonpromotional(self):
        proof = self.seal()
        self.assertEqual(proof["nativeTestsPassed"], 2)
        self.assertEqual(proof["testNames"], ["BridgeAbi", "LocusGeometry"])
        self.assertFalse(proof["independentComtradeLaneReplaced"])
        self.assertFalse(proof["releasePromotionAuthority"])
        self.assertEqual(self.verify(), proof)

    def test_rejects_missing_native_tests(self):
        self.junit.write_text('<testsuite tests="0" />', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "no test suites|empty"):
            self.seal()

    def test_rejects_failed_or_skipped_ctest(self):
        for snippet in (
            GOOD_JUNIT.replace('failures="0"', 'failures="1"'),
            GOOD_JUNIT.replace('<testcase name="BridgeAbi" classname="native" time="0.1" />',
                               '<testcase name="BridgeAbi"><failure /></testcase>'),
            GOOD_JUNIT.replace('skipped="0"', 'skipped="1"'),
            GOOD_JUNIT.replace('<testcase name="BridgeAbi" classname="native" time="0.1" />',
                               '<testcase name="BridgeAbi"><skipped /></testcase>'),
        ):
            with self.subTest(snippet=snippet[:80]):
                self.junit.write_text(snippet, encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "nonzero|failing"):
                    self.seal()

    def test_rejects_counter_mismatch(self):
        self.junit.write_text(GOOD_JUNIT.replace('tests="2"', 'tests="3"'), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "counter disagrees"):
            self.seal()

    def test_rejects_duplicate_tests(self):
        self.junit.write_text(GOOD_JUNIT.replace("LocusGeometry", "BridgeAbi"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate"):
            self.seal()

    def test_rejects_tampered_ctest_xml_and_bridge(self):
        self.seal()
        self.junit.write_text(GOOD_JUNIT.replace("BridgeAbi", "OtherTest"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "does not match"):
            self.verify()
        self.junit.write_text(GOOD_JUNIT, encoding="utf-8")
        self.bridge.write_bytes(b"tampered-bridge")
        with self.assertRaisesRegex(ValueError, "does not match"):
            self.verify()

    def test_rejects_stale_identity_or_tampered_manifest(self):
        self.seal()
        self.args["source_sha"] = "d" * 40
        with self.assertRaisesRegex(ValueError, "does not match"):
            self.verify()
        self.args["source_sha"] = "a" * 40
        proof = json.loads(self.output.read_text(encoding="utf-8"))
        proof["nativeTestsPassed"] = 999
        self.output.write_text(json.dumps(proof), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "does not match"):
            self.verify()

    def test_rejects_broken_or_unsafe_xml(self):
        for value in ('not xml', '<!DOCTYPE foo><testsuite tests="0"/>'):
            with self.subTest(value=value):
                self.junit.write_text(value, encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "valid XML|prohibited"):
                    self.seal()

    def test_canonical_workflow_captures_native_junit_without_replacing_comtrade(self):
        root = Path(__file__).resolve().parents[1]
        native = (root / "scripts/build-ardirec-bridge.ps1").read_text(encoding="utf-8")
        build = (root / ".github/workflows/build.yml").read_text(encoding="utf-8")
        comtrade = (root / ".github/workflows/comtrade-viewer-integration.yml").read_text(encoding="utf-8")
        for token in ("CTestJunitPath", "--output-junit", "ctest --test-dir"):
            self.assertIn(token, native)
        for token in ("-CTestJunitPath", "write-ci-native-ctest-proof.py",
                      "ci-native-ctest-authority.json", "ctest.xml"):
            self.assertIn(token, build)
        for token in ("Build and test pinned native bridge", "build-ardirec-bridge.ps1",
                      "Enforce field-tested native-only ARSAS COMTRADE routing"):
            self.assertIn(token, comtrade)

if __name__ == "__main__":
    unittest.main()
