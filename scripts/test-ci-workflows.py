#!/usr/bin/env python3
"""Offline tests for the CI-P0 workflow inventory and integrity guard."""
from __future__ import annotations

import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

SOURCE = Path(__file__).with_name("audit-ci-workflows.py")
spec = importlib.util.spec_from_file_location("arsas_ci_inventory", SOURCE)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CiWorkflowInventoryTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        subprocess.run(["git", "-C", str(self.root), "init", "-q"], check=True)

    def tearDown(self):
        self.tmp.cleanup()

    def write(self, path: str, body: str) -> None:
        dest = self.root / path
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(body, encoding="utf-8")

    def commit(self) -> None:
        subprocess.run(["git", "-C", str(self.root), "add", "-A"], check=True)
        subprocess.run(
            [
                "git",
                "-C",
                str(self.root),
                "-c",
                "user.email=test@example.invalid",
                "-c",
                "user.name=CI P0 Test",
                "commit",
                "-qm",
                "synthetic fixture",
            ],
            check=True,
        )

    def test_inventory_counts_expensive_primitives_deterministically(self):
        self.write(
            ".github/workflows/check.yml",
            """name: Check
on:
  pull_request:
  workflow_dispatch:
jobs:
  build:
    runs-on: windows-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7
      - name: Setup
        uses: actions/setup-dotnet@v6
      - name: Build
        run: |
          dotnet restore App.sln
          dotnet build App.sln --no-restore
          dotnet test Tests.csproj --no-build
""",
        )
        self.commit()

        first = module.inventory(self.root)
        second = module.inventory(self.root)
        self.assertEqual(
            json.dumps(first, sort_keys=True),
            json.dumps(second, sort_keys=True),
        )
        self.assertEqual(first["totals"]["workflowFiles"], 1)
        self.assertEqual(first["totals"]["actionsCheckout"], 1)
        self.assertEqual(first["totals"]["actionsSetupDotnet"], 1)
        self.assertEqual(first["totals"]["dotnetRestore"], 1)
        self.assertEqual(first["totals"]["dotnetBuild"], 1)
        self.assertEqual(first["totals"]["dotnetTest"], 1)
        self.assertEqual(first["totals"]["workflowsWithDotnetBuild"], 1)
        self.assertEqual(first["totals"]["workflowsWithDotnetTest"], 1)
        self.assertEqual(first["totals"]["pullRequestWorkflows"], 1)
        self.assertEqual(first["structureErrors"], [])

    def test_duplicate_step_name_inside_same_job_is_rejected(self):
        self.write(
            ".github/workflows/corrupt.yml",
            """name: Corrupt
on:
  pull_request:
jobs:
  build:
    runs-on: windows-latest
    steps:
      - name: Setup .NET
        run: echo first
      - name: Setup .NET
        run: echo duplicate
""",
        )
        self.commit()

        report = module.inventory(self.root)
        self.assertFalse(not report["structureErrors"])
        self.assertTrue(
            any("duplicate step name 'Setup .NET'" in item for item in report["structureErrors"])
        )

    def test_same_step_name_in_different_jobs_is_allowed(self):
        self.write(
            ".github/workflows/parallel.yml",
            """name: Parallel
on: [push, pull_request]
jobs:
  first:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7
  second:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v7
""",
        )
        self.commit()

        report = module.inventory(self.root)
        self.assertEqual(report["structureErrors"], [])
        self.assertEqual(report["totals"]["actionsCheckout"], 2)

    def test_duplicate_job_id_is_rejected(self):
        self.write(
            ".github/workflows/duplicate-job.yml",
            """name: Duplicate Job
on:
  push:
jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - name: First
        run: echo first
  validate:
    runs-on: windows-latest
    steps:
      - name: Second
        run: echo second
""",
        )
        self.commit()

        report = module.inventory(self.root)
        self.assertTrue(
            any("duplicate job id 'validate'" in item for item in report["structureErrors"])
        )

    def test_p3h_windows_pr_workflows_cancel_only_superseded_pr_runs(self):
        repo = Path(__file__).resolve().parents[1]
        workflows = [
            ".github/workflows/build.yml",
            ".github/workflows/installer-windows.yml",
            ".github/workflows/smart-discovery-mainline-readiness.yml",
            ".github/workflows/rcb-export-guard.yml",
            ".github/workflows/validate-sv-evidence.yml",
            ".github/workflows/validate-io-testing.yml",
            ".github/workflows/comtrade-viewer-integration.yml",
            ".github/workflows/smart-discovery-golden-provenance.yml",
            ".github/workflows/smart-discovery-golden-budget-lock.yml",
            ".github/workflows/smart-discovery-repeat-run-stability.yml",
        ]
        expected_group = (
            "group: ${{ github.workflow }}-"
            "${{ github.event.pull_request.number || github.run_id }}"
        )
        expected_cancel = (
            "cancel-in-progress: ${{ github.event_name == 'pull_request' }}"
        )
        for relative in workflows:
            source = (repo / relative).read_text(encoding="utf-8")
            self.assertIn(expected_group, source, relative)
            self.assertIn(expected_cancel, source, relative)
            self.assertNotIn("cancel-in-progress: true", source, relative)

        release = (repo / ".github/workflows/release-windows.yml").read_text(
            encoding="utf-8"
        )
        self.assertNotIn(expected_cancel, release)

    def test_budget_rejects_new_duplicate_build_cost(self):
        self.write(
            ".github/workflows/build.yml",
            """name: Build
on:
  pull_request:
jobs:
  build:
    runs-on: windows-latest
    steps:
      - name: Build
        run: dotnet build App.sln
""",
        )
        self.write(
            "budget.json",
            json.dumps(
                {
                    "schemaVersion": 1,
                    "baselineSourceCommit": "synthetic",
                    "limits": {"dotnetBuild": 0},
                }
            ),
        )
        self.commit()

        report = module.evaluate(self.root, self.root / "budget.json")
        self.assertFalse(report["isHealthy"])
        self.assertIn(
            "CI budget exceeded: dotnetBuild=1, allowed maximum=0.",
            report["budget"]["violations"],
        )


if __name__ == "__main__":
    unittest.main()
