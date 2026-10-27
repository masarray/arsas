#!/usr/bin/env python3
"""Offline tests for the CI-P0 workflow inventory and integrity guard."""
from __future__ import annotations

import importlib.util
import fnmatch
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

    def test_sv_evidence_trigger_only_runs_for_sv_domain(self):
        """SV canonical proof must run for SV edits, not every application test."""
        repo = Path(__file__).resolve().parents[1]
        source = (repo / ".github/workflows/validate-sv-evidence.yml").read_text(
            encoding="utf-8"
        )
        # This workflow uses a simple quoted on.pull_request.paths YAML sequence.
        # Extract exactly that sequence, not push or workflow_dispatch paths.
        header = "  pull_request:\n    paths:\n"
        self.assertEqual(source.count(header), 1)
        block = source.split(header, 1)[1].split("  workflow_dispatch:", 1)[0]
        paths = [
            json.loads(line.strip()[2:])
            for line in block.splitlines()
            if line.strip().startswith('- "')
        ]
        self.assertEqual(len(paths), len(set(paths)))
        self.assertNotIn("tests/ARSAS.Tests/**", paths)

        def matches(changed_path: str) -> bool:
            # GitHub path filters match segments: "*" cannot cross "/".
            # Current authority resolver uses a terminal "/**" for descendants.
            for pattern in paths:
                if pattern.endswith("/**"):
                    if changed_path.startswith(pattern[:-3] + "/"):
                        return True
                    continue
                self.assertNotIn("**", pattern, pattern)
                candidate = changed_path.split("/")
                glob = pattern.split("/")
                if len(candidate) == len(glob) and all(
                    fnmatch.fnmatchcase(part, rule)
                    for part, rule in zip(candidate, glob)
                ):
                    return True
            return False

        for changed_path in (
            "tests/ARSAS.Tests/SmvFastWorkflowTests.cs",
            "tests/ARSAS.Tests/SmvSnapshotEvidenceExporterTests.cs",
            "tests/ARSAS.Tests/SampledValueFutureRegressionTests.cs",
            "Services/SmvSnapshotCaptureService.cs",
            "Services/SmvSnapshotSafetyAssessment.cs",
            "Services/SmvViewerWindow.P1Evidence.cs",
            "tests/ARSAS.Tests/SmvSnapshotEvidenceExporterReviewTests.cs",
            "tests/ARSAS.Tests/ProcessBusRoutingRegressionTests.cs",
            "MainWindow.IedSmvQuickStart.cs",
            "SmvViewerWindow.xaml.cs",
            "engines/ARIEC61850.lock.json",
            ".github/actions/resolve-ariec-engine/action.yml",
            ".github/workflows/build.yml",
            "scripts/write-ci-canonical-proof.py",
            "scripts/verify-ci-canonical-reuse.py",
        ):
            self.assertTrue(matches(changed_path), changed_path)

        for changed_path in (
            "tests/ARSAS.Tests/M2IedBrowserHierarchyAndReportWireTests.cs",
            "tests/ARSAS.Tests/IoFatStructuredMmsAliasRegressionTests.cs",
            "tests/ARSAS.Tests/RcbExportEvidenceAcquisitionRegressionTests.cs",
            "Models/IedBrowserPresentation.cs",
            "Services/RcbAvailabilityProbeService.cs",
            "Services/nested/SmvSnapshotCaptureService.cs",
            "tests/ARSAS.Tests/nested/SmvFastWorkflowTests.cs",
            "docs/ARSAS_IED_BROWSER_M2_EXECUTION_ARD.md",
        ):
            self.assertFalse(matches(changed_path), changed_path)

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


    def test_io_domain_trigger_covers_all_real_owners_without_unrelated_ci(self):
        """Fail closed on missed IO/FAT owners and slash-spanning wildcard mistakes."""

        repo = Path(__file__).resolve().parents[1]
        source = (repo / ".github/workflows/validate-io-testing.yml").read_text(
            encoding="utf-8"
        )
        header = "  pull_request:\n    paths:\n"
        self.assertEqual(source.count(header), 1)
        block = source.split(header, 1)[1].split("  workflow_dispatch:", 1)[0]
        paths = [
            json.loads(line.strip()[2:])
            for line in block.splitlines()
            if line.strip().startswith('- "')
        ]
        self.assertEqual(len(paths), len(set(paths)))
        self.assertNotIn("tests/ARSAS.Tests/**", paths)
        self.assertNotIn(".github/workflows/**", paths)

        def matches(changed: str) -> bool:
            for pattern in paths:
                if pattern.endswith("/**"):
                    if changed.startswith(pattern[:-3] + "/"):
                        return True
                    continue
                self.assertNotIn("**", pattern, pattern)
                parts, glob = changed.split("/"), pattern.split("/")
                if len(parts) == len(glob) and all(
                    fnmatch.fnmatchcase(part, rule)
                    for part, rule in zip(parts, glob)
                ):
                    return True
            return False

        # Every tracked domain-owned source/test must be routed to IO proof.
        owned = []
        for pattern in (
            "MainWindow.IoTesting*.cs",
            "MainWindow.NativeFat*.cs",
            "MainWindow.P0Fat*.cs",
            "IoListTestingWindow*.cs",
            "IoListTestingWindow.xaml",
            "Models/IoTesting/*.cs",
            "Models/NativeFat*.cs",
            "Services/IoTesting/*.cs",
            "tests/ARSAS.Tests/Io*.cs",
            "tests/ARSAS.Tests/Fat*.cs",
            "tests/ARSAS.Tests/NativeFat*.cs",
        ):
            owned.extend(repo.glob(pattern))
        self.assertGreater(len(owned), 130)
        for path in owned:
            self.assertTrue(matches(path.relative_to(repo).as_posix()), path)

        for path in (
            "MainWindow.IoTesting.FutureRecovery.cs",
            "IoListTestingWindow.FutureSafety.cs",
            "Models/IoTesting/NextSession.cs",
            "Services/IoTesting/new/IoFatFutureSafety.cs",
            "tests/ARSAS.Tests/NativeFatFutureRegressionTests.cs",
            "tests/ARSAS.Tests/FatFutureSafetyTests.cs",
            "tests/ARSAS.Tests/IoFutureRegressionTests.cs",
            "engines/ARIEC61850.lock.json",
            ".github/actions/resolve-ariec-engine/action.yml",
            ".github/workflows/build.yml",
            "scripts/write-ci-canonical-proof.py",
            "scripts/verify-ci-canonical-reuse.py",
            "scripts/test-ci-canonical-reuse.py",
        ):
            self.assertTrue(matches(path), path)

        for path in (
            "tests/ARSAS.Tests/M2IedBrowserHierarchyAndReportWireTests.cs",
            "tests/ARSAS.Tests/SmvSnapshotEvidenceExporterTests.cs",
            "tests/ARSAS.Tests/RcbExportEvidenceAcquisitionRegressionTests.cs",
            "Services/SmvSnapshotCaptureService.cs",
            "Services/RcbAvailabilityProbeService.cs",
            "MainWindow.IedBrowser.cs",
            "docs/ARSAS_IED_BROWSER_M2_EXECUTION_ARD.md",
            "tests/ARSAS.Tests/nested/IoTestSafetyTests.cs",
            "Services/nested/IoTesting/IoFatFutureSafety.cs",
        ):
            self.assertFalse(matches(path), path)

    def test_p4_docs_only_pr_preflight_never_fails_open(self):
        """Exact verified doc-only PRs may skip Windows; any doubt runs Windows."""
        repo = Path(__file__).resolve().parents[1]
        workflow = (repo / '.github/workflows/build.yml').read_text(encoding='utf-8')
        scope = workflow.split('  pr-scope:\n', 1)[1].split('  build-windows:\n', 1)[0]
        build = workflow.split('  build-windows:\n', 1)[1]
        for contract in (
            "if: github.event_name == 'pull_request'",
            'runs-on: ubuntu-latest',
            'pull-requests: read',
            'uses: actions/github-script@v7',
            'github.paginate(github.rest.pulls.listFiles',
            'per_page: 100',
            'files.length > 0 && files.length < 3000',
            "filename === 'README.md'",
            "filename.startsWith('docs/')",
            "filename.endsWith('.md')",
            "filename !== 'docs/INTEROPERABILITY_REFERENCE_CONTRACT.md'",
            "status === 'added' || status === 'modified'",
            '!previous_filename',
            'catch (error)',
            "core.setOutput('needs_windows', needsWindows ? 'true' : 'false')",
        ):
            self.assertIn(contract, scope)
        self.assertNotIn('actions/checkout@', scope)
        self.assertNotIn('dotnet ', scope)
        self.assertIn('needs: [pr-scope]', build)
        self.assertIn('always() &&', build)
        self.assertIn("github.event_name != 'pull_request'", build)
        self.assertIn("needs.pr-scope.result != 'success'", build)
        self.assertIn("needs.pr-scope.outputs.needs_windows != 'false'", build)
        self.assertNotIn("needs.pr-scope.outputs.needs_windows == 'true'", build)
        self.assertEqual(workflow.count('  build-windows:\n'), 1)
        self.assertIn('name: Upload sealed Windows package input', build)
        self.assertIn('Upload directly downloadable portable EXE (no ZIP)', build)
        self.assertIn('archive: false', build)
        self.assertIn('ref: ${{ github.sha }}', build)
        self.assertIn('  push:\n    branches: [ main ]', workflow)
        self.assertIn('  workflow_dispatch:', workflow)

    def test_p4_capture_requires_opt_in_and_retains_manual_exact_sha_proof(self):
        """An unlabeled Smart Discovery PR must not spend a 2nd Windows runner."""
        repo = Path(__file__).resolve().parents[1]
        src = (repo / '.github/workflows/smart-discovery-capture-build.yml').read_text(encoding='utf-8')
        self.assertIn('types: [opened, synchronize, reopened, ready_for_review, labeled]', src)
        self.assertIn('contains(github.event.pull_request.labels.*.name, \'ci-field-capture\')', src)
        self.assertIn('!github.event.pull_request.draft', src)
        self.assertIn("github.event_name == 'workflow_dispatch'", src)
        self.assertIn("needs.manual-regression.result == 'success'", src)
        self.assertIn('    needs: [manual-regression]', src)
        self.assertIn('      source_sha: ${{ github.sha }}', src)
        self.assertIn('      shadow_mode: true', src)
        self.assertIn('    runs-on: windows-latest', src)
        self.assertIn('cancel-in-progress: true', src)
        self.assertIn('name: Build smart-discovery portable field capture', src)


    def test_p4_execute_actual_api_script_and_job_conditions(self):
        """Execute extracted P4 code and job if-expressions on Node, no GitHub CI."""
        import shutil
        import textwrap

        self.assertIsNotNone(shutil.which("node"), "Ubuntu CI P0 needs Node.js")
        repo = Path(__file__).resolve().parents[1]
        build = (repo / ".github/workflows/build.yml").read_text(encoding="utf-8")
        capture = (repo / ".github/workflows/smart-discovery-capture-build.yml").read_text(encoding="utf-8")
        scope = build.split("  pr-scope:\n", 1)[1].split("  build-windows:\n", 1)[0]
        js_script = textwrap.dedent(scope.split("          script: |\n", 1)[1]).strip()

        def condition(source, job, after):
            body = source.split("  " + job + ":\n", 1)[1]
            raw = body.split("    if: >-\n", 1)[1].split("    " + after + ":", 1)[0]
            raw = textwrap.dedent(raw).strip()
            self.assertTrue(raw.startswith("$" + "{{") and raw.endswith("}}"))
            return raw[3:-2].strip()

        def changed(name, status="modified", **extra):
            return dict(filename=name, status=status, **extra)

        fixtures = {
            "script": js_script,
            "win": condition(build, "build-windows", "runs-on"),
            "capture": condition(capture, "build-smart-capture", "name"),
            "files": [
                dict(name="readme", files=[changed("README.md")], expected="false"),
                dict(name="docs", files=[changed("docs/A.md", "added"), changed("docs/B.md")], expected="false"),
                dict(name="source", files=[changed("Models/MonitorModels.cs")], expected="true"),
                dict(name="mixed", files=[changed("README.md"), changed("Services/Runtime.cs")], expected="true"),
                dict(name="protected", files=[changed("docs/INTEROPERABILITY_REFERENCE_CONTRACT.md")], expected="true"),
                dict(name="rename_source", files=[changed("docs/A.md", "renamed", previous_filename="Services/Main.cs")], expected="true"),
                dict(name="rename_docs", files=[changed("docs/A.md", "renamed", previous_filename="docs/B.md")], expected="true"),
                dict(name="removed", files=[changed("docs/A.md", "removed")], expected="true"),
                dict(name="empty", files=[], expected="true"),
                dict(name="workflow", files=[changed(".github/workflows/build.yml")], expected="true"),
                dict(name="truncated", files=[changed("docs/" + str(i) + ".md") for i in range(3000)], expected="true"),
                dict(name="api_failure", files=[changed("README.md")], fail=True, expected="true"),
            ],
            "windows": [
                dict(name="docs_ok", event="pull_request", result="success", output="false", expected=False),
                dict(name="source", event="pull_request", result="success", output="true", expected=True),
                dict(name="no_output", event="pull_request", result="success", output="", expected=True),
                dict(name="failed", event="pull_request", result="failure", output="", expected=True),
                dict(name="skipped", event="pull_request", result="skipped", output="", expected=True),
                dict(name="push", event="push", result="skipped", output="", expected=True),
                dict(name="manual", event="workflow_dispatch", result="skipped", output="", expected=True),
            ],
            "field": [
                dict(name="unlabeled", event="pull_request", draft=False, labels=[], manual="skipped", expected=False),
                dict(name="irrelevant", event="pull_request", draft=False, labels=["other"], manual="skipped", expected=False),
                dict(name="labeled", event="pull_request", draft=False, labels=["ci-field-capture"], manual="skipped", expected=True),
                dict(name="draft", event="pull_request", draft=True, labels=["ci-field-capture"], manual="skipped", expected=False),
                dict(name="manual_ok", event="workflow_dispatch", manual="success", expected=True),
                dict(name="manual_fail", event="workflow_dispatch", manual="failure", expected=False),
                dict(name="manual_missing", event="workflow_dispatch", manual="skipped", expected=False),
            ],
        }

        runner = r'''
const fs = require("node:fs"), vm = require("node:vm");
const input = JSON.parse(fs.readFileSync(0, "utf8"));
const equal = (got, want, name) => {
  if (got !== want) throw Error(name + ": got=" + got + " expected=" + want);
};
(async () => {
  const classify = vm.runInNewContext("(async (github, context, core) => {\n" + input.script + "\n})");
  for (const c of input.files) {
    const emitted = {};
    const github = {
      rest: {pulls: {listFiles() {}}},
      paginate: async (method, args) => {
        if (args.per_page !== 100 || args.pull_number !== 123) throw Error("bad pagination");
        if (c.fail) throw Error("synthetic API failure");
        return c.files;
      }
    };
    const context = {repo: {owner: "owner", repo: "arsas"}, issue: {number: 123}};
    const core = {info() {}, warning() {}, setOutput(k,v) {emitted[k] = v;}};
    await classify(github, context, core);
    equal(emitted.needs_windows, c.expected, "classification/" + c.name);
  }
  const windows = input.win.replaceAll("needs.pr-scope", "needs.prScope");
  const capture = input.capture.replaceAll("needs.manual-regression", "needs.manualRegression")
    .replaceAll("github.event.pull_request.labels.*.name",
                "github.event.pull_request.labels.map(x => x.name)");
  for (const c of input.windows) {
    const context = {
      always: () => true, github: {event_name: c.event},
      needs: {prScope: {result: c.result, outputs: {needs_windows: c.output}}}
    };
    equal(Boolean(vm.runInNewContext("(" + windows + ")", context)), c.expected, "Windows/" + c.name);
  }
  for (const c of input.field) {
    const context = {
      always: () => true,
      github: {event_name: c.event, event: {pull_request: {
        draft: c.draft, labels: (c.labels || []).map(name => ({name}))
      }}},
      contains: (values, target) => values.includes(target),
      needs: {manualRegression: {result: c.manual}}
    };
    equal(Boolean(vm.runInNewContext("(" + capture + ")", context)), c.expected, "Field/" + c.name);
  }
  console.log("CI-P4 actual-source policy expressions: 26/26 PASS");
})().catch(e => { console.error(e.stack || e); process.exitCode=1; });
'''
        completed = subprocess.run(
            ["node", "-e", runner], input=json.dumps(fixtures), text=True,
            capture_output=True, timeout=20, check=False
        )
        self.assertEqual(0, completed.returncode, completed.stdout + "\n" + completed.stderr)
        self.assertIn("26/26 PASS", completed.stdout)

if __name__ == "__main__":
    unittest.main()
