#!/usr/bin/env python3
"""Offline tests for the read-only maintenance consumer inventory."""
import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

SOURCE = Path(__file__).with_name("audit-maintenance-dependencies.py")
spec = importlib.util.spec_from_file_location("arsas_inventory", SOURCE)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class InventoryTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        subprocess.run(["git", "-C", str(self.root), "init", "-q"], check=True)

    def tearDown(self):
        self.tmp.cleanup()

    def write(self, path, body):
        dest = self.root / path
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(body, encoding="utf-8")

    def commit(self):
        subprocess.run(["git", "-C", str(self.root), "add", "-A"], check=True)
        subprocess.run(["git", "-C", str(self.root), "-c", "user.email=test@example.invalid",
            "-c", "user.name=Inventory Test", "commit", "-qm", "synthetic fixture"], check=True)

    def test_workflow_manual_and_review_candidate_are_separate(self):
        self.write("scripts/build-sample.py", 'print("sample")\n')
        self.write("scripts/no-reference.ps1", 'Write-Host "sample"\n')
        self.write(".github/workflows/check.yml",
            "name: Check\non:\n  push:\n  workflow_dispatch:\njobs:\n  t:\n    steps:\n      - run: python scripts/build-sample.py\n")
        self.write("docs/howto.md", "Manually run build-sample.py.\n")
        self.write("docs/misleading.md", "Not build-sample.py.old or xbuild-sample.py\n")
        self.commit()
        data = module.inventory(self.root)
        self.assertEqual(data["counts"]["workflows"], 1)
        self.assertEqual(data["counts"]["scripts"], 2)
        self.assertEqual(data["workflows"][0]["events"], ["push", "workflow_dispatch"])
        self.assertEqual(data["workflows"][0]["referencedScripts"], ["scripts/build-sample.py"])
        lookup = {x["path"]: x for x in data["scripts"]}
        self.assertEqual(lookup["scripts/build-sample.py"]["workflowReferences"],
                         [".github/workflows/check.yml"])
        self.assertEqual(lookup["scripts/build-sample.py"]["otherTrackedReferences"],
                         ["docs/howto.md"])
        self.assertEqual(data["requiresManualConsumerReview"], ["scripts/no-reference.ps1"])
        self.assertIn("NOT mean unused", data["interpretation"])
        self.assertEqual(json.dumps(data, sort_keys=True),
                         json.dumps(module.inventory(self.root), sort_keys=True))

    def test_windows_path_and_inline_yaml_events(self):
        self.write("scripts/run-check.ps1", 'Write-Host "x"\n')
        self.write(".github/workflows/check.yml",
            "on: [push, pull_request]\njobs:\n  t:\n    steps:\n      - run: .\\scripts\\run-check.ps1\n")
        self.commit()
        data = module.inventory(self.root)
        self.assertEqual(data["workflows"][0]["events"], ["pull_request", "push"])
        self.assertEqual(data["workflows"][0]["referencedScripts"], ["scripts/run-check.ps1"])

if __name__ == "__main__":
    unittest.main()
