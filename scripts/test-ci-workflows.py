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
            "${{ github.event_name == 'pull_request' && "
            "github.event.pull_request.number || github.run_id }}"
        )
        expected_cancel = "cancel-in-progress: true"
        for relative in workflows:
            source = (repo / relative).read_text(encoding="utf-8")
            self.assertIn(expected_group, source, relative)
            self.assertIn(expected_cancel, source, relative)

        release = (repo / ".github/workflows/release-windows.yml").read_text(
            encoding="utf-8"
        )
        self.assertNotIn(expected_group, release)

    def test_p3i_main_and_manual_verification_runs_are_not_superseded(self):
        """PR updates may cancel old PR work; main and manual runs must keep their proof."""
        repo = Path(__file__).resolve().parents[1]
        isolated = {
            "ci-p0-workflow-integrity.yml": "ci-p0-workflow-integrity",
            "smart-discovery-capture-build.yml": "smart-discovery-field-capture",
            "scl-interoperability-r7.yml": "scl-interoperability-r7",
            "smart-discovery-merge-execution-guard.yml": "smart-discovery-merge-execution",
            "smart-discovery-production-promotion.yml": "smart-discovery-production-promotion",
            "interoperability-reference-guard.yml": "interoperability-reference",
        }
        pr_or_unique_run = (
            "${{ github.event_name == 'pull_request' && "
            "github.event.pull_request.number || github.run_id }}"
        )
        for filename, prefix in isolated.items():
            source = (repo / ".github/workflows" / filename).read_text(
                encoding="utf-8"
            )
            self.assertEqual(source.count("\nconcurrency:\n"), 1, filename)
            block = source.split("\nconcurrency:\n", 1)[1].split("\njobs:\n", 1)[0]
            self.assertIn(f"group: {prefix}-{pr_or_unique_run}", block, filename)
            self.assertIn("cancel-in-progress: true", block, filename)
            self.assertNotIn("|| github.ref", block, filename)

        post_merge = (
            repo / ".github/workflows/smart-discovery-post-merge-production.yml"
        ).read_text(encoding="utf-8")
        block = post_merge.split("\nconcurrency:\n", 1)[1].split(
            "\njobs:\n", 1
        )[0]
        self.assertIn(
            "group: smart-discovery-post-merge-production-${{ github.run_id }}",
            block,
        )
        self.assertIn("cancel-in-progress: false", block)
        self.assertNotIn("github.ref", block)

    def test_p4_docs_only_pr_preflight_preserves_fail_closed_build_authority(self):
        """Docs may skip one Windows job; code, engine, CI, missing API and
        main/manual must still retain canonical exact-SHA package authority."""
        repo = Path(__file__).resolve().parents[1]
        source = (repo / ".github/workflows/build.yml").read_text(encoding="utf-8")
        self.assertIn("  push:\n    branches: [ main ]", source)
        self.assertIn("  workflow_dispatch:", source)
        self.assertIn("  pr-scope:", source)
        self.assertIn("uses: actions/github-script@v7", source)
        self.assertIn("github.paginate(github.rest.pulls.listFiles", source)
        self.assertIn("pull-requests: read", source)
        self.assertIn("changed.length > 0 && changed.length < 3000", source)
        self.assertIn("filename === 'README.md'", source)
        self.assertIn("filename.startsWith('docs/')", source)
        self.assertIn("filename.endsWith('.md')", source)
        self.assertIn("filename !== 'docs/INTEROPERABILITY_REFERENCE_CONTRACT.md'", source)
        self.assertIn("catch (error)", source)
        self.assertIn("core.setOutput('needs_windows', full ? 'true' : 'false')", source)
        self.assertIn("needs.pr-scope.outputs.needs_windows == 'true'", source)
        self.assertIn("github.event_name != 'pull_request'", source)
        self.assertIn("always() &&", source)
        self.assertEqual(source.count("  build-windows:\n"), 1)
        self.assertIn("name: Build, test, validate and package Windows application", source)
        self.assertIn("ref: ${{ github.sha }}", source)
        self.assertIn("ARSAS-win-x64-portable-single-exe", source)

    def test_p4_field_capture_is_opt_in_but_manual_gate_is_intact(self):
        repo = Path(__file__).resolve().parents[1]
        source = (repo / ".github/workflows/smart-discovery-capture-build.yml").read_text(
            encoding="utf-8"
        )
        self.assertIn("types: [opened, synchronize, reopened, labeled]", source)
        self.assertIn("  workflow_dispatch:", source)
        self.assertIn("contains(github.event.pull_request.labels.*.name, 'ci-field-capture')",
                      source)
        self.assertIn("github.event_name == 'workflow_dispatch'", source)
        self.assertIn("needs.manual-regression.result == 'success'", source)
        self.assertIn("  manual-regression:", source)
        self.assertIn("shadow_mode: true", source)
        self.assertIn("  build-smart-capture:", source)
        self.assertIn("name: Build smart-discovery portable field capture", source)
        self.assertIn("require-native-ctest-proof", source)
        self.assertIn("Smoke test portable executable", source)
        self.assertIn("Upload smart field-capture build", source)
        self.assertIn("cancel-in-progress: true", source)

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
