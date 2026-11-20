#!/usr/bin/env python3
"""Pure synthetic negative/positive tests for isolated reporting and legacy trials."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("trial_resolver", Path(__file__).with_name("resolve-ariec-trial.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class TrialTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "engines").mkdir()
        self.pin = {"repository":"masarray/ARIEC61850","ref":"main","commit":"a"*40}
        self.legacy = {"commit":"b"*40,"runMode":"fast-dev-nonpromotion","scope":"command-read"}
        self.report = {"schemaVersion":1,"repository":"masarray/ARIEC61850",
                       "commit":"c"*40,"approvedBaselineCommit":"a"*40,
                       "runMode":"fast-dev-nonpromotion","scope":"reporting",
                       "mainlinePromotionForbidden":True,"physicalQualificationRequired":True,
                       "cyclicMmsProcessPollingForbidden":True,"automaticRcbMutationForbidden":True}
        self.write()

    def write(self):
        for name, data in (("ARIEC61850.lock.json", self.pin),
                           ("ARIEC61850.trial.json", self.legacy),
                           ("ARIEC61850.reporting-trial.json", self.report)):
            (self.root / "engines" / name).write_text(json.dumps(data), encoding="utf-8")

    def test_selected_reporting_and_existing_command(self):
        self.assertEqual(module.resolve(self.root, {module.REPORT}), ("c"*40,"reporting"))
        self.assertEqual(module.resolve(self.root, set()), ("b"*40,"command-read"))

    def test_competing_manifests_rejected(self):
        with self.assertRaises(ValueError):
            module.resolve(self.root, {module.CMD,module.REPORT})

    def test_wrong_baseline_or_unsafe_policy_rejected(self):
        for key,value in (("approvedBaselineCommit","d"*40),("commit","a"*40),
                          ("mainlinePromotionForbidden",False),
                          ("physicalQualificationRequired",False),
                          ("cyclicMmsProcessPollingForbidden",False),
                          ("automaticRcbMutationForbidden",False),
                          ("scope","command-read"),("commit","staging")):
            original = self.report[key]
            self.report[key] = value
            self.write()
            with self.assertRaises(ValueError):
                module.resolve(self.root, {module.REPORT})
            self.report[key] = original
        self.write()


if __name__ == "__main__":
    unittest.main()
