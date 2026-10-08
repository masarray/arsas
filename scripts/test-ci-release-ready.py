#!/usr/bin/env python3
"""Offline fail-closed fixtures for CI-P3G release readiness."""
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def load(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / filename)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


gate = load("release_ready_gate", "wait-ci-release-ready.py")
S = "a" * 40
E = "b" * 40
A = "c" * 40
BUILD = 111
INSTALLER = 222
VERSION = "1.6.40"


def run_obj(run_id: int, name: str, status: str = "completed", conclusion: str | None = "success"):
    return {
        "id": run_id,
        "run_attempt": 1,
        "name": name,
        "event": "push",
        "head_branch": "main",
        "head_sha": S,
        "status": status,
        "conclusion": conclusion,
    }


class FakeApi:
    def __init__(
        self,
        *,
        build_status="completed",
        build_conclusion="success",
        installer_status="completed",
        installer_conclusion="success",
        package_count=1,
        installer_count=1,
    ):
        self.build = run_obj(BUILD, "Build ARSAS", build_status, build_conclusion)
        self.installer = run_obj(
            INSTALLER,
            "Validate ARSAS Windows installer",
            installer_status,
            installer_conclusion,
        )
        self.package_count = package_count
        self.installer_count = installer_count

    def get(self, url: str):
        if "workflows/build.yml/runs?" in url:
            return {"workflow_runs": [self.build]}
        if "workflows/installer-windows.yml/runs?" in url:
            return {"workflow_runs": [self.installer]}
        if f"/runs/{BUILD}/artifacts" in url:
            return {
                "artifacts": [
                    {
                        "id": 300 + i,
                        "name": "ARSAS-windows-package-input",
                        "expired": False,
                        "workflow_run": {"id": BUILD},
                    }
                    for i in range(self.package_count)
                ]
            }
        if f"/runs/{INSTALLER}/artifacts" in url:
            return {
                "artifacts": [
                    {
                        "id": 400 + i,
                        "name": f"ARSAS-{VERSION}-win-x64-installer",
                        "expired": False,
                        "workflow_run": {"id": INSTALLER},
                    }
                    for i in range(self.installer_count)
                ]
            }
        raise RuntimeError(url)


def identity():
    return {
        "sourceSha": S,
        "version": VERSION,
        "engineSha": E,
        "ardirecSha": A,
        "refType": "branch",
        "refName": "main",
    }


class ReleaseReadyTests(unittest.TestCase):
    def test_accepts_only_when_package_and_completed_installer_metadata_are_ready(self):
        proof = gate.wait_release_ready(
            FakeApi(),
            repository="masarray/arsas",
            identity=identity(),
            wait_seconds=0,
            poll_seconds=1,
        )
        self.assertTrue(proof["metadataReady"])
        self.assertFalse(proof["releasePromotionAuthority"])
        self.assertEqual(proof["buildRunId"], BUILD)
        self.assertEqual(proof["installerRunId"], INSTALLER)
        self.assertEqual(proof["packageArtifactId"], 300)
        self.assertEqual(proof["installerArtifactId"], 400)

    def test_completed_failed_build_is_immediate_hard_failure(self):
        with self.assertRaisesRegex(gate.ReleaseReadyError, "Build ARSAS run failed"):
            gate.wait_release_ready(
                FakeApi(build_conclusion="failure"),
                repository="masarray/arsas",
                identity=identity(),
                wait_seconds=0,
                poll_seconds=1,
            )

    def test_completed_failed_installer_is_immediate_hard_failure(self):
        with self.assertRaisesRegex(gate.ReleaseReadyError, "installer validation failed"):
            gate.wait_release_ready(
                FakeApi(installer_conclusion="failure"),
                repository="masarray/arsas",
                identity=identity(),
                wait_seconds=0,
                poll_seconds=1,
            )

    def test_in_progress_installer_is_not_ready(self):
        with self.assertRaisesRegex(gate.ReleaseReadyError, "Timed out"):
            gate.wait_release_ready(
                FakeApi(installer_status="in_progress", installer_conclusion=None),
                repository="masarray/arsas",
                identity=identity(),
                wait_seconds=0,
                poll_seconds=1,
            )

    def test_missing_package_is_not_ready_while_build_runs(self):
        with self.assertRaisesRegex(gate.ReleaseReadyError, "Timed out"):
            gate.wait_release_ready(
                FakeApi(
                    build_status="in_progress",
                    build_conclusion=None,
                    package_count=0,
                    installer_status="in_progress",
                    installer_conclusion=None,
                ),
                repository="masarray/arsas",
                identity=identity(),
                wait_seconds=0,
                poll_seconds=1,
            )

    def test_duplicate_package_or_installer_metadata_fails_closed(self):
        with self.assertRaisesRegex(gate.ReleaseReadyError, "exactly one"):
            gate.wait_release_ready(
                FakeApi(package_count=2),
                repository="masarray/arsas",
                identity=identity(),
                wait_seconds=0,
                poll_seconds=1,
            )
        with self.assertRaisesRegex(gate.ReleaseReadyError, "exactly one"):
            gate.wait_release_ready(
                FakeApi(installer_count=2),
                repository="masarray/arsas",
                identity=identity(),
                wait_seconds=0,
                poll_seconds=1,
            )

    def test_wrong_head_branch_or_event_never_matches(self):
        api = FakeApi()
        api.build["head_branch"] = "release"
        api.installer["event"] = "workflow_dispatch"
        with self.assertRaisesRegex(gate.ReleaseReadyError, "Timed out"):
            gate.wait_release_ready(
                api,
                repository="masarray/arsas",
                identity=identity(),
                wait_seconds=0,
                poll_seconds=1,
            )

    def test_identity_resolution_binds_main_manifest_version_and_locks(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / ".release").mkdir()
            (root / "engines").mkdir()
            (root / "VERSION").write_text(VERSION + "\n", encoding="utf-8")
            (root / "Directory.Build.props").write_text(
                f"<Project><PropertyGroup><Version>{VERSION}</Version></PropertyGroup></Project>",
                encoding="utf-8",
            )
            (root / "ArIED61850Tester.csproj").write_text(
                f"<Project><PropertyGroup><Version>{VERSION}</Version></PropertyGroup></Project>",
                encoding="utf-8",
            )
            (root / ".release/windows.json").write_text(
                json.dumps({"version": VERSION}), encoding="utf-8"
            )
            (root / "engines/ARIEC61850.lock.json").write_text(
                json.dumps(
                    {
                        "repository": "masarray/ARIEC61850",
                        "ref": "main",
                        "commit": E,
                    }
                ),
                encoding="utf-8",
            )
            (root / "engines/ARDIREC.lock.json").write_text(
                json.dumps(
                    {
                        "schema": 3,
                        "repository": "masarray/ardirec",
                        "ref": "feature/test",
                        "commit": A,
                        "bridge": {"abi": 1, "mode": "native-only"},
                    }
                ),
                encoding="utf-8",
            )
            resolved = gate.resolve_identity(
                root, source_sha=S, ref_type="branch", ref_name="main"
            )
            self.assertEqual(resolved["version"], VERSION)
            self.assertEqual(resolved["engineSha"], E)
            self.assertEqual(resolved["ardirecSha"], A)

    def test_release_workflow_defers_windows_but_retains_full_authority_verifiers(self):
        workflow = (ROOT.parent / ".github/workflows/release-windows.yml").read_text(
            encoding="utf-8"
        )
        for token in (
            "release-ready:",
            "runs-on: ubuntu-latest",
            "wait-ci-release-ready.py",
            "needs: [release-ready]",
            "github.event_name == 'workflow_dispatch'",
            "verify-ci-package-reuse.py",
            "verify-ci-installer-reuse.py",
            "Smoke-test exact release portable single EXE",
            "Smoke-test silent installer and uninstaller",
            "Generate SPDX 2.3 package SBOM",
            "Create or update GitHub Release",
        ):
            self.assertIn(token, workflow)
        self.assertIn(
            "authority: scheduling metadata only; Windows release revalidates all artifact bytes",
            workflow,
        )

    def test_tag_identity_must_match_canonical_version(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            (root / ".release").mkdir()
            (root / "engines").mkdir()
            (root / "VERSION").write_text(VERSION, encoding="utf-8")
            xml = f"<Project><PropertyGroup><Version>{VERSION}</Version></PropertyGroup></Project>"
            (root / "Directory.Build.props").write_text(xml, encoding="utf-8")
            (root / "ArIED61850Tester.csproj").write_text(xml, encoding="utf-8")
            (root / ".release/windows.json").write_text(
                json.dumps({"version": VERSION}), encoding="utf-8"
            )
            (root / "engines/ARIEC61850.lock.json").write_text(
                json.dumps({"repository": "masarray/ARIEC61850", "ref": "main", "commit": E}),
                encoding="utf-8",
            )
            (root / "engines/ARDIREC.lock.json").write_text(
                json.dumps(
                    {
                        "schema": 3,
                        "repository": "masarray/ardirec",
                        "commit": A,
                        "bridge": {"abi": 1, "mode": "native-only"},
                    }
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(gate.ReleaseReadyError, "version identity mismatch"):
                gate.resolve_identity(
                    root, source_sha=S, ref_type="tag", ref_name="v9.9.9"
                )


if __name__ == "__main__":
    unittest.main()
