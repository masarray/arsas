#!/usr/bin/env python3
"""Offline fail-closed tests for CI-P3 canonical Windows package sealing/reuse."""
from __future__ import annotations

import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parent


def load(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / filename)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


producer = load("arsas_package_producer", "write-ci-package-manifest.py")
verifier = load("arsas_package_verifier", "verify-ci-package-reuse.py")

SOURCE = "a" * 40
ENGINE = "b" * 40
ARDIREC = "c" * 40
HEAD = "d" * 40
BRANCH = "ci/p3-canonical-windows-package"
RUN = 4321
TRX = b"""<?xml version="1.0" encoding="UTF-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <ResultSummary outcome="Completed">
    <Counters total="1320" executed="1320" passed="1320" failed="0"
      error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0"
      notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="1320"
      inProgress="0" pending="0" />
  </ResultSummary>
</TestRun>"""


def canonical_manifest(event_name: str = "pull_request") -> dict:
    counters = producer.counters_from_trx(TRX)
    return {
        "schemaVersion": 1,
        "kind": "arsas-canonical-regression-evidence",
        "canonicalWorkflow": "Build ARSAS",
        "workflowRunId": RUN,
        "runAttempt": 1,
        "eventName": event_name,
        "exactSourceSha": SOURCE,
        "exactEngineSha": ENGINE,
        "testFile": "arsas-tests.trx",
        "testTrxSha256": hashlib.sha256(TRX).hexdigest(),
        "testCounters": counters,
        "fullRegressionPassed": True,
        "releasePromotionAuthority": False,
    }


def make_payload(event_name: str = "pull_request") -> tuple[bytes, dict]:
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        evidence = root / "evidence-source"
        evidence.mkdir()
        (evidence / "ci-canonical-authority.json").write_text(
            json.dumps(canonical_manifest(event_name), sort_keys=True, indent=2) + "\n",
            encoding="utf-8",
        )
        (evidence / "arsas-tests.trx").write_bytes(TRX)

        portable = root / "ARSAS-1.6.40-win-x64-portable.exe"
        portable.write_bytes(b"portable-tested-bits")

        installer = root / "ARSAS-1.6.40-win-x64"
        bridge = installer / "Tools" / "ArdIrec" / "ardirec_bridge.dll"
        bridge.parent.mkdir(parents=True)
        bridge.write_bytes(b"native-bridge")
        (installer / "ARSAS.exe").write_bytes(b"folder-app")
        (installer / "LICENSE").write_text("license", encoding="utf-8")

        portable_identity = root / "ARSAS-1.6.40-win-x64-portable-build-identity.json"
        portable_identity.write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "kind": "arsas-portable-build-identity",
                    "version": "1.6.40",
                    "runtime": "win-x64",
                    "sourceCommit": SOURCE,
                    "engineCommit": ENGINE,
                    "ardIrecLockCommit": ARDIREC,
                    "ardIrecBridgeSha256": hashlib.sha256(bridge.read_bytes()).hexdigest(),
                    "ardIrecBridgeSizeBytes": bridge.stat().st_size,
                    "portableSha256": hashlib.sha256(portable.read_bytes()).hexdigest(),
                    "portableSizeBytes": portable.stat().st_size,
                    "deterministicManagedBuild": True,
                    "reproducibleNativeLinkRequested": True,
                },
                sort_keys=True,
            )
            + "\n",
            encoding="utf-8",
        )

        verification = root / "verification-source"
        tests = verification / "ARSAS.Tests"
        fixtures = verification / "fixtures"
        tests.mkdir(parents=True)
        fixtures.mkdir(parents=True)
        (tests / "ARSAS.Tests.dll").write_bytes(b"test-assembly")
        (tests / ".coverage-transient").write_text("must-not-promote", encoding="utf-8")
        (fixtures / "minimal_1999.cfg").write_text("cfg", encoding="utf-8")
        (fixtures / "distance_p1.cfg").write_text("locus", encoding="utf-8")
        (fixtures / "p1-release-smoke.cfg").write_text("release-cfg", encoding="utf-8")
        (fixtures / "p1-release-smoke.dat").write_bytes(b"release-dat")

        artifact = root / "artifact"
        manifest = producer.stage_and_create_manifest(
            artifact_root=artifact,
            portable=portable,
            portable_identity=portable_identity,
            installer_input=installer,
            verification_dir=verification,
            evidence_dir=evidence,
            source_sha=SOURCE,
            engine_sha=ENGINE,
            ardirec_sha=ARDIREC,
            version="1.6.40",
            runtime="win-x64",
            workflow_run_id=RUN,
            run_attempt=1,
            event_name=event_name,
            test_assembly_relative="ARSAS.Tests/ARSAS.Tests.dll",
            comtrade_fixture_relative="fixtures/minimal_1999.cfg",
            locus_fixture_relative="fixtures/distance_p1.cfg",
            release_fixture_relative="fixtures/p1-release-smoke.cfg",
        )

        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for path in sorted(artifact.rglob("*")):
                if path.is_file():
                    archive.write(path, "payload/" + path.relative_to(artifact).as_posix())
        return buffer.getvalue(), manifest


class FakeApi:
    def __init__(
        self,
        *,
        event_name: str = "pull_request",
        status: str = "completed",
        conclusion: str | None = "success",
        artifact_available: bool = True,
        binary_404_count: int = 0,
    ):
        self.blob, _ = make_payload(event_name)
        self.artifact_available = artifact_available
        self.binary_404_count = binary_404_count
        self.runs = [{
            "id": RUN,
            "run_attempt": 1,
            "name": "Build ARSAS",
            "head_sha": HEAD,
            "head_branch": BRANCH,
            "event": event_name,
            "status": status,
            "conclusion": conclusion,
        }]

    def get(self, url: str, *, binary: bool = False):
        if binary:
            if self.binary_404_count > 0:
                self.binary_404_count -= 1
                raise verifier.GitHubRequestError(
                    "GitHub package evidence request failed: HTTP 404 Not Found",
                    status=404,
                )
            return self.blob
        if "/workflows/build.yml/runs?" in url:
            return {"workflow_runs": self.runs}
        if "/runs/" in url and "/artifacts" in url:
            if not self.artifact_available:
                return {"artifacts": []}
            return {"artifacts": [{
                "id": 99,
                "name": "ARSAS-windows-package-input",
                "expired": False,
                "archive_download_url": "https://example.invalid/package.zip",
                "workflow_run": {"id": RUN},
            }]}
        raise RuntimeError("Unexpected fake API URL " + url)


class PackageReuseTests(unittest.TestCase):
    def test_producer_and_verifier_accept_exact_payload(self):
        blob, manifest = make_payload()
        proof = verifier.validate_package_archive(
            blob,
            source_sha=SOURCE,
            engine_sha=ENGINE,
            ardirec_sha=ARDIREC,
            workflow_run_id=RUN,
            run_attempt=1,
            event_name="pull_request",
        )
        self.assertEqual(proof["passed"], 1320)
        self.assertEqual(proof["portableSha256"], manifest["portable"]["sha256"])
        self.assertEqual(proof["portablePath"], manifest["portable"]["path"])
        self.assertEqual(
            proof["portableIdentityPath"],
            manifest["portableIdentity"]["path"],
        )
        self.assertEqual(proof["nativeBridgeSha256"], manifest["nativeBridge"]["sha256"])
        self.assertEqual(
            proof["releaseFixturePath"],
            manifest["verification"]["releaseFixturePath"],
        )

    def test_safe_materialization_occurs_after_verification(self):
        blob, _ = make_payload()
        with tempfile.TemporaryDirectory() as temp:
            out = Path(temp) / "out"
            verifier.validate_package_archive(
                blob,
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                workflow_run_id=RUN,
                run_attempt=1,
                event_name="pull_request",
                output_dir=out,
            )
            self.assertTrue((out / "ci-windows-package-authority.json").is_file())
            self.assertTrue((out / "installer-input/ARSAS-1.6.40-win-x64/ARSAS.exe").is_file())

    def test_rejects_tampered_portable_build_identity(self):
        blob, _ = make_payload()
        src = zipfile.ZipFile(io.BytesIO(blob))
        buffer = io.BytesIO()
        with src, zipfile.ZipFile(buffer, "w") as dst:
            for info in src.infolist():
                data = src.read(info)
                if info.filename.endswith("portable-build-identity.json"):
                    identity = json.loads(data)
                    identity["sourceCommit"] = "f" * 40
                    data = json.dumps(identity, sort_keys=True).encode()
                dst.writestr(info.filename, data)
        with self.assertRaisesRegex(
            verifier.PackageProofError,
            "size differs|digest differs|Portable build identity",
        ):
            verifier.validate_package_archive(
                buffer.getvalue(),
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                workflow_run_id=RUN,
                run_attempt=1,
                event_name="pull_request",
            )

    def test_rejects_wrong_ardirec_identity(self):
        blob, _ = make_payload()
        with self.assertRaisesRegex(verifier.PackageProofError, "identity or authority"):
            verifier.validate_package_archive(
                blob,
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha="f" * 40,
                workflow_run_id=RUN,
                run_attempt=1,
                event_name="pull_request",
            )

    def test_rejects_tampered_payload_bytes(self):
        blob, _ = make_payload()
        src = zipfile.ZipFile(io.BytesIO(blob))
        buffer = io.BytesIO()
        with src, zipfile.ZipFile(buffer, "w") as dst:
            for info in src.infolist():
                data = src.read(info)
                if info.filename.endswith("ARSAS-1.6.40-win-x64-portable.exe"):
                    data += b"tamper"
                dst.writestr(info.filename, data)
        with self.assertRaisesRegex(verifier.PackageProofError, "size differs|digest differs"):
            verifier.validate_package_archive(
                buffer.getvalue(),
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                workflow_run_id=RUN,
                run_attempt=1,
                event_name="pull_request",
            )

    def test_rejects_unsafe_archive_path(self):
        blob, _ = make_payload()
        src = zipfile.ZipFile(io.BytesIO(blob))
        buffer = io.BytesIO()
        with src, zipfile.ZipFile(buffer, "w") as dst:
            for info in src.infolist():
                dst.writestr(info.filename, src.read(info))
            dst.writestr("../escape.txt", b"bad")
        with self.assertRaisesRegex(verifier.PackageProofError, "unsafe|outside"):
            verifier.validate_package_archive(
                buffer.getvalue(),
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                workflow_run_id=RUN,
                run_attempt=1,
                event_name="pull_request",
            )

    def test_rejects_windows_casefold_archive_collision(self):
        blob, _ = make_payload()
        src = zipfile.ZipFile(io.BytesIO(blob))
        buffer = io.BytesIO()
        with src, zipfile.ZipFile(buffer, "w") as dst:
            names = src.namelist()
            for info in src.infolist():
                dst.writestr(info.filename, src.read(info))
            target = next(
                name for name in names
                if name.endswith("installer-input/ARSAS-1.6.40-win-x64/ARSAS.exe")
            )
            dst.writestr(target[:-9] + "arsas.exe", b"collision")
        with self.assertRaisesRegex(
            verifier.PackageProofError, "Windows-normalized path collision"
        ):
            verifier.validate_package_archive(
                buffer.getvalue(),
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                workflow_run_id=RUN,
                run_attempt=1,
                event_name="pull_request",
            )

    def test_rejects_windows_trailing_dot_archive_path(self):
        blob, _ = make_payload()
        src = zipfile.ZipFile(io.BytesIO(blob))
        buffer = io.BytesIO()
        with src, zipfile.ZipFile(buffer, "w") as dst:
            for info in src.infolist():
                dst.writestr(info.filename, src.read(info))
            prefix = PurePosixPath(src.namelist()[0]).parts[0]
            dst.writestr(f"{prefix}/installer-input/bad./payload.txt", b"bad")
        with self.assertRaisesRegex(
            verifier.PackageProofError, "Windows-unsafe path component"
        ):
            verifier.validate_package_archive(
                buffer.getvalue(),
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                workflow_run_id=RUN,
                run_attempt=1,
                event_name="pull_request",
            )

    def test_manifest_never_claims_installer_or_release_authority(self):
        _, manifest = make_payload()
        self.assertIs(manifest["installerBuilt"], False)
        self.assertIs(manifest["releasePromotionAuthority"], False)
        self.assertTrue(manifest["nativeBridgeIntegrationPassed"])
        self.assertTrue(manifest["portableSmokePassed"])

    def test_hidden_build_transients_are_not_promoted(self):
        blob, manifest = make_payload()
        self.assertFalse(any("/." in "/" + entry["path"] for entry in manifest["files"]))
        with zipfile.ZipFile(io.BytesIO(blob)) as archive:
            self.assertFalse(any("/." in "/" + name for name in archive.namelist()))

    def test_installer_script_and_workflow_share_prebuilt_test_contract(self):
        installer_script = (ROOT / "build-windows-installer.ps1").read_text(encoding="utf-8")
        workflow = (ROOT.parent / ".github/workflows/installer-windows.yml").read_text(encoding="utf-8")
        for token in (
            "[string]$TestAssemblyPath",
            "[string]$FixtureCfg",
            "[string]$LocusFixtureCfg",
            "& dotnet vstest $TestAssemblyPath",
        ):
            self.assertIn(token, installer_script)
        for token in (
            "-TestAssemblyPath $env:CANONICAL_TEST_ASSEMBLY",
            "-FixtureCfg $env:CANONICAL_COMTRADE_FIXTURE",
            "-LocusFixtureCfg $env:CANONICAL_LOCUS_FIXTURE",
            "& dotnet vstest $env:CANONICAL_TEST_ASSEMBLY",
        ):
            self.assertIn(token, workflow)

    def test_in_progress_exact_artifact_can_be_reused(self):
        api = FakeApi(status="in_progress", conclusion=None)
        proof = verifier.verify_canonical_package(
            api,
            repository="masarray/arsas",
            branch=BRANCH,
            head_sha=HEAD,
            source_sha=SOURCE,
            engine_sha=ENGINE,
            ardirec_sha=ARDIREC,
            wait_seconds=0,
            poll_seconds=1,
            allow_in_progress_artifact=True,
        )
        self.assertEqual(proof["proofStage"], "sealed-package-artifact-ready")
        self.assertFalse(proof["workflowCompleted"])

    def test_transient_artifact_archive_404_is_retried_without_fallback(self):
        api = FakeApi(
            status="in_progress",
            conclusion=None,
            binary_404_count=2,
        )
        original_sleep = verifier.time.sleep
        try:
            verifier.time.sleep = lambda _: None
            proof = verifier.verify_canonical_package(
                api,
                repository="masarray/arsas",
                branch=BRANCH,
                head_sha=HEAD,
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                wait_seconds=2,
                poll_seconds=1,
                allow_in_progress_artifact=True,
            )
        finally:
            verifier.time.sleep = original_sleep
        self.assertEqual(proof["proofStage"], "sealed-package-artifact-ready")
        self.assertEqual(api.binary_404_count, 0)

    def test_persistent_artifact_archive_404_times_out_fail_closed(self):
        api = FakeApi(
            status="in_progress",
            conclusion=None,
            binary_404_count=100,
        )
        with self.assertRaisesRegex(
            verifier.PackageProofError,
            "Timed out waiting for exact canonical Windows package artifact",
        ):
            verifier.verify_canonical_package(
                api,
                repository="masarray/arsas",
                branch=BRANCH,
                head_sha=HEAD,
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                wait_seconds=0,
                poll_seconds=1,
                allow_in_progress_artifact=True,
            )

    def test_non_404_artifact_download_error_remains_hard_failure(self):
        api = FakeApi(status="in_progress", conclusion=None)
        original_get = api.get

        def failing_get(url: str, *, binary: bool = False):
            if binary:
                raise verifier.GitHubRequestError(
                    "GitHub package evidence request failed: HTTP 403 Forbidden",
                    status=403,
                )
            return original_get(url, binary=binary)

        api.get = failing_get
        with self.assertRaisesRegex(verifier.GitHubRequestError, "HTTP 403"):
            verifier.verify_canonical_package(
                api,
                repository="masarray/arsas",
                branch=BRANCH,
                head_sha=HEAD,
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                wait_seconds=0,
                poll_seconds=1,
                allow_in_progress_artifact=True,
            )

    def test_completed_failed_latest_run_is_never_reused(self):
        api = FakeApi(status="completed", conclusion="failure")
        with self.assertRaisesRegex(verifier.PackageProofError, "Latest canonical Build ARSAS run failed"):
            verifier.verify_canonical_package(
                api,
                repository="masarray/arsas",
                branch=BRANCH,
                head_sha=HEAD,
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                wait_seconds=0,
                poll_seconds=1,
                allow_in_progress_artifact=True,
            )

    def test_missing_in_progress_artifact_fails_closed_at_bound(self):
        api = FakeApi(status="in_progress", conclusion=None, artifact_available=False)
        with self.assertRaisesRegex(verifier.PackageProofError, "Timed out"):
            verifier.verify_canonical_package(
                api,
                repository="masarray/arsas",
                branch=BRANCH,
                head_sha=HEAD,
                source_sha=SOURCE,
                engine_sha=ENGINE,
                ardirec_sha=ARDIREC,
                wait_seconds=0,
                poll_seconds=1,
                allow_in_progress_artifact=True,
            )


if __name__ == "__main__":
    unittest.main()
