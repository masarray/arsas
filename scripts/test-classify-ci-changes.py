#!/usr/bin/env python3
"""Offline policy/regression tests for advisory CI-P1 PR classification."""
from __future__ import annotations

import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

SOURCE = Path(__file__).with_name("classify-ci-changes.py")
spec = importlib.util.spec_from_file_location("arsas_p1_classifier", SOURCE)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ClassifierTests(unittest.TestCase):
    def test_trusted_scl_change_requires_full_tests_and_iec(self):
        r = module.classify(["Services/NativeIec61850Client.SclAssisted.cs"])
        self.assertTrue(r["flags"]["build"])
        self.assertTrue(r["flags"]["tests"])
        self.assertTrue(r["flags"]["iec"])
        self.assertFalse(r["flags"]["sv"])
        self.assertTrue(r["requiresLegacyAuthority"])

    def test_sv_and_goose_change_is_process_bus(self):
        r = module.classify(["Services/SmvSnapshotCaptureService.cs"])
        self.assertTrue(r["flags"]["sv"])
        self.assertTrue(r["flags"]["build"])
        r2 = module.classify(["Services/GooseSubscriberRuntime.cs"])
        self.assertTrue(r2["flags"]["sv"])

    def test_fat_io_change_is_targeted(self):
        r = module.classify(["Services/IoTesting/IoFatOrchestrator.cs"])
        self.assertTrue(r["flags"]["io"])
        self.assertTrue(r["flags"]["tests"])
        self.assertFalse(r["flags"]["iec"])

    def test_engine_lock_change_is_broad(self):
        r = module.classify(["engines/ARIEC61850.lock.json"])
        self.assertEqual(r["risk"], "broad")
        self.assertTrue(all(r["flags"][name] for name in ("build", "tests", "iec", "io", "sv")))

    def test_pure_documentation_skips_dotnet_advisory(self):
        r = module.classify(["docs/audits/OTHER.md", "README.md"])
        self.assertFalse(r["flags"]["build"])
        self.assertEqual(r["risk"], "none")
        self.assertEqual(r["areas"], ["docs"])

    def test_ci_change_detected_separately_from_application(self):
        r = module.classify([".github/workflows/ci-p1-shadow.yml"])
        self.assertTrue(r["flags"]["ci"])
        self.assertFalse(r["flags"]["build"])
        self.assertFalse(r["requiresLegacyAuthority"])

    def test_scripts_outside_ci_infrastructure_are_conservative(self):
        r = module.classify(["scripts/publish-windows-portable.ps1"])
        self.assertTrue(r["flags"]["build"])

    def test_unknown_extension_fails_open_to_full_build(self):
        r = module.classify(["config/custom-device.assetbin"])
        self.assertTrue(r["flags"]["unknown"])
        self.assertTrue(r["flags"]["iec"])
        self.assertTrue(r["flags"]["tests"])
        self.assertEqual(r["risk"], "broad")

    def test_empty_paths_do_not_silently_skip(self):
        r = module.classify([])
        self.assertTrue(r["flags"]["unknown"])
        self.assertTrue(r["flags"]["build"])

    def test_dedup_and_order_are_deterministic(self):
        paths = ["README.md", "Services/MmsReporting.cs", "README.md"]
        self.assertEqual(
            json.dumps(module.classify(paths), sort_keys=True),
            json.dumps(module.classify(list(reversed(paths))), sort_keys=True),
        )
        self.assertEqual(len(module.classify(paths)["changedFiles"]), 2)

    def test_pr_merge_diff_uses_first_parent_and_falls_back_if_absent(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            def git(*args: str) -> None:
                subprocess.run(["git", "-C", str(root), *args], check=True,
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            git("init", "-q")
            git("config", "user.name", "CI Test")
            git("config", "user.email", "test@example.invalid")
            (root / "README.md").write_text("base\n")
            git("add", "-A")
            git("commit", "-qm", "base")
            self.assertEqual(
                module.merge_diff(root), ["__unclassified_source_change__"]
            )
            base_branch = subprocess.check_output(
                ["git", "-C", str(root), "branch", "--show-current"]
            ).decode("utf-8").strip()
            git("checkout", "-qb", "feature")
            (root / "Services").mkdir()
            (root / "Services" / "MmsReporting.cs").write_text("// change\n")
            git("add", "-A")
            git("commit", "-qm", "feature")
            git("checkout", "-q", base_branch)
            git("merge", "--no-ff", "-qm", "merge feature", "feature")
            self.assertEqual(
                module.merge_diff(root), ["Services/MmsReporting.cs"]
            )


if __name__ == "__main__":
    unittest.main()
