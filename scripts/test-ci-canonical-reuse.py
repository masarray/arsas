#!/usr/bin/env python3
"""Synthetic fail-closed tests for canonical CI proof production and consumption."""
from __future__ import annotations

import importlib.util
import io
import json
import unittest
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def load(name: str, path: str):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


producer = load("arsas_canonical_producer", "write-ci-canonical-proof.py")
consumer = load("arsas_canonical_consumer", "verify-ci-canonical-reuse.py")
SOURCE = "a" * 40
ENGINE = "b" * 40
HEAD = "c" * 40
BRANCH = "ci/p2a-canonical-regression-reuse"
RUN = 1234

TRX = b"""<?xml version="1.0" encoding="UTF-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <ResultSummary outcome="Completed">
    <Counters total="1320" executed="1320" passed="1320" failed="0"
      error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0"
      notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="1320"
      inProgress="0" pending="0" />
  </ResultSummary>
  <Results>
    <UnitTestResult testName="ARSAS.Tests.SmvSnapshotEvidenceExporterTests.ExportsEvidence" outcome="Passed" />
    <UnitTestResult testName="ARSAS.Tests.IoTestWorkflowTests.BuildsPlan" outcome="Passed" />
    <UnitTestResult testName="ARSAS.Tests.IoListParserTests.ParsesRows" outcome="Passed" />
    <UnitTestResult testName="ARSAS.Tests.IoFatOrchestratorTests.RunsFat" outcome="Passed" />
    <UnitTestResult testName="ARSAS.Tests.FatDataSetPlannerTests.BuildsStaticDataSet" outcome="Passed" />
    <UnitTestResult testName="ARSAS.Tests.FatSclProjectionTests.ProjectsScl" outcome="Passed" />
  </Results>
</TestRun>"""


def fixture(event_name: str = "pull_request"):
    return producer.create_manifest(
        source_sha=SOURCE,
        checkout_sha=SOURCE,
        engine_expected=ENGINE,
        engine_actual=ENGINE,
        workflow_run_id=RUN,
        run_attempt=1,
        event_name=event_name,
        trx=TRX,
        trx_name="arsas-tests.trx",
    )


def zip_archive(manifest: dict, trx: bytes = TRX, extra: dict | None = None) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        archive.writestr("ARSAS/TestResults/ci-canonical-authority.json", json.dumps(manifest))
        archive.writestr("ARSAS/TestResults/arsas-tests.trx", trx)
        for name, content in (extra or {}).items():
            archive.writestr(name, content)
    return buffer.getvalue()


class FakeApi:
    def __init__(
        self,
        run_status: str = "completed",
        result: str = "success",
        artifact_available: bool = True,
        event_name: str = "pull_request",
        branch: str = BRANCH,
        head_sha: str = HEAD,
        source_sha: str = SOURCE,
    ):
        self.status, self.result = run_status, result
        self.artifact_available = artifact_available
        self.event_name = event_name
        self.branch = branch
        self.head_sha = head_sha
        manifest = fixture(event_name=event_name)
        if source_sha != SOURCE:
            manifest = dict(manifest)
            manifest["exactSourceSha"] = source_sha
        self.blob = zip_archive(manifest)
        self.listing = [
            {"id": RUN, "run_attempt": 1, "name": "Build ARSAS",
             "head_sha": head_sha, "head_branch": branch, "event": event_name,
             "status": self.status, "conclusion": self.result}
        ]

    def get(self, url: str, binary: bool = False):
        if binary:
            return self.blob
        if "/workflows/build.yml/runs?" in url:
            return {"workflow_runs": self.listing}
        if "/runs/" in url and "/artifacts" in url:
            if not self.artifact_available:
                return {"artifacts": []}
            return {"artifacts": [
                {"id": 22, "name": "ARSAS-test-evidence", "expired": False,
                 "archive_download_url": "https://example.invalid/artifact.zip",
                 "workflow_run": {"id": RUN}}
            ]}
        raise RuntimeError("Unexpected test API request " + url)


def run_once(
    api: FakeApi,
    *,
    allow_in_progress_artifact: bool = False,
    event_name: str = "pull_request",
    branch: str = BRANCH,
    head_sha: str = HEAD,
    source_sha: str = SOURCE,
    required_test_substrings: list[str] | None = None,
):
    return consumer.verify_canonical(
        api,
        repository="masarray/arsas",
        branch=branch,
        head_sha=head_sha,
        merge_sha=source_sha,
        engine_sha=ENGINE,
        wait_seconds=0,
        poll_seconds=1,
        allow_in_progress_artifact=allow_in_progress_artifact,
        event_name=event_name,
        required_test_substrings=required_test_substrings,
    )


class CanonicalReuseTests(unittest.TestCase):
    def test_producer_and_consumer_accept_verified_exact_evidence(self):
        proof = run_once(FakeApi())
        self.assertEqual(proof["passed"], 1320)
        self.assertEqual(proof["failed"], 0)
        self.assertEqual(proof["mergeSha"], SOURCE)
        self.assertEqual(proof["engineSha"], ENGINE)

    def test_manifest_never_claims_release_authority(self):
        self.assertIs(fixture()["releasePromotionAuthority"], False)
        self.assertTrue(fixture()["fullRegressionPassed"])

    def test_required_domain_test_family_is_proven_from_canonical_trx(self):
        proof = run_once(
            FakeApi(),
            required_test_substrings=[
                "SmvSnapshotEvidenceExporterTests",
                "IoTest",
                "IoList",
                "IoFat",
                "FatDataSet",
                "FatScl",
            ],
        )
        self.assertEqual(proof["requiredTests"]["SmvSnapshotEvidenceExporterTests"], 1)
        self.assertEqual(proof["requiredTests"]["IoTest"], 1)
        self.assertEqual(proof["requiredTests"]["FatScl"], 1)

    def test_missing_required_domain_test_family_fails_closed(self):
        with self.assertRaisesRegex(consumer.ProofError, "missing required test family"):
            run_once(
                FakeApi(),
                required_test_substrings=["DefinitelyMissingTests"],
            )

    def test_required_domain_test_family_rejects_nonpassing_match(self):
        bad = TRX.replace(
            b'testName="ARSAS.Tests.IoTestWorkflowTests.BuildsPlan" outcome="Passed"',
            b'testName="ARSAS.Tests.IoTestWorkflowTests.BuildsPlan" outcome="Failed"',
        )
        manifest = producer.create_manifest(
            source_sha=SOURCE,
            checkout_sha=SOURCE,
            engine_expected=ENGINE,
            engine_actual=ENGINE,
            workflow_run_id=RUN,
            run_attempt=1,
            event_name="pull_request",
            trx=bad,
            trx_name="arsas-tests.trx",
        )
        api = FakeApi()
        api.blob = zip_archive(manifest, bad)
        with self.assertRaisesRegex(consumer.ProofError, "not entirely Passed"):
            run_once(api, required_test_substrings=["IoTest"])

    def test_reject_wrong_merge_sha(self):
        api = FakeApi()
        with self.assertRaisesRegex(consumer.ProofError, "stale/different source revision|Timed out"):
            consumer.validate_artifact_archive(
                api.blob, merge_sha="f" * 40, engine_sha=ENGINE,
                workflow_run_id=RUN, run_attempt=1,
            )

    def test_reject_wrong_engine_sha(self):
        with self.assertRaisesRegex(consumer.ProofError, "engine SHA"):
            consumer.validate_artifact_archive(
                FakeApi().blob, merge_sha=SOURCE, engine_sha="f" * 40,
                workflow_run_id=RUN, run_attempt=1,
            )

    def test_reject_manifest_run_attempt_mismatch(self):
        with self.assertRaisesRegex(consumer.ProofError, "run attempt"):
            consumer.validate_artifact_archive(
                FakeApi().blob, merge_sha=SOURCE, engine_sha=ENGINE,
                workflow_run_id=RUN, run_attempt=2,
            )

    def test_reject_changed_trx_bytes(self):
        api = FakeApi()
        api.blob = zip_archive(fixture(), TRX + b"malicious addition")
        with self.assertRaisesRegex(consumer.ProofError, "TRX digest"):
            run_once(api)

    def test_reject_nonpassing_trx_even_with_matching_hash(self):
        bad = TRX.replace(b'failed="0"', b'failed="1"').replace(b'passed="1320"', b'passed="1319"')
        with self.assertRaisesRegex(ValueError, "not entirely green"):
            producer.counters_from_trx(bad)

    def test_reject_unsafe_archive_entry(self):
        with self.assertRaisesRegex(consumer.ProofError, "unsafe"):
            consumer.validate_artifact_archive(
                zip_archive(fixture(), extra={"../../do-not-extract.txt": "unsafe"}),
                merge_sha=SOURCE, engine_sha=ENGINE,
                workflow_run_id=RUN, run_attempt=1,
            )

    def test_reject_duplicate_manifest(self):
        blob = zip_archive(
            fixture(), extra={"duplicate/ci-canonical-authority.json": json.dumps(fixture())}
        )
        with self.assertRaisesRegex(consumer.ProofError, "duplicate"):
            consumer.validate_artifact_archive(
                blob, merge_sha=SOURCE, engine_sha=ENGINE,
                workflow_run_id=RUN, run_attempt=1,
            )

    def test_latest_failed_canonical_run_must_reject_even_if_older_passed(self):
        api = FakeApi(result="failure")
        api.listing.append(
            {"id": RUN - 1, "run_attempt": 1, "name": "Build ARSAS",
             "head_sha": HEAD, "head_branch": BRANCH, "event": "pull_request",
             "status": "completed", "conclusion": "success"}
        )
        with self.assertRaisesRegex(consumer.ProofError, "Latest canonical.*failed"):
            run_once(api)

    def test_pending_run_fails_closed_at_timeout(self):
        with self.assertRaisesRegex(consumer.ProofError, "Timed out"):
            run_once(FakeApi(run_status="in_progress", result=None))

    def test_artifact_ready_mode_accepts_exact_all_pass_proof_while_packaging_runs(self):
        proof = run_once(
            FakeApi(run_status="in_progress", result=None),
            allow_in_progress_artifact=True,
        )
        self.assertEqual(proof["proofStage"], "full-regression-artifact-ready")
        self.assertIs(proof["workflowCompleted"], False)
        self.assertIs(proof["packagingSmokeProven"], False)
        self.assertEqual(proof["canonicalRunStatus"], "in_progress")
        self.assertEqual(proof["passed"], 1320)

    def test_artifact_ready_mode_still_rejects_completed_failed_run(self):
        with self.assertRaisesRegex(consumer.ProofError, "Latest canonical.*failed"):
            run_once(
                FakeApi(run_status="completed", result="failure"),
                allow_in_progress_artifact=True,
            )

    def test_artifact_ready_mode_waits_when_expected_artifact_is_not_uploaded(self):
        with self.assertRaisesRegex(consumer.ProofError, "artifact-ready canonical"):
            run_once(
                FakeApi(run_status="in_progress", result=None, artifact_available=False),
                allow_in_progress_artifact=True,
            )

    def test_main_push_accepts_exact_same_sha_artifact_ready_proof(self):
        main_sha = "d" * 40
        api = FakeApi(
            run_status="in_progress",
            result=None,
            event_name="push",
            branch="main",
            head_sha=main_sha,
            source_sha=main_sha,
        )
        proof = run_once(
            api,
            allow_in_progress_artifact=True,
            event_name="push",
            branch="main",
            head_sha=main_sha,
            source_sha=main_sha,
        )
        self.assertEqual(proof["eventName"], "push")
        self.assertEqual(proof["branch"], "main")
        self.assertEqual(proof["mergeSha"], main_sha)
        self.assertEqual(proof["proofStage"], "full-regression-artifact-ready")
        self.assertFalse(proof["packagingSmokeProven"])

    def test_main_push_rejects_pr_manifest_event_mismatch(self):
        main_sha = "d" * 40
        api = FakeApi(
            run_status="completed",
            result="success",
            event_name="push",
            branch="main",
            head_sha=main_sha,
            source_sha=main_sha,
        )
        api.blob = zip_archive(fixture(event_name="pull_request"))
        with self.assertRaisesRegex(consumer.ProofError, "event mismatch"):
            run_once(
                api,
                event_name="push",
                branch="main",
                head_sha=main_sha,
                source_sha=main_sha,
            )

    def test_main_push_rejects_wrong_branch_or_sha(self):
        main_sha = "d" * 40
        api = FakeApi(
            event_name="push",
            branch="main",
            head_sha=main_sha,
            source_sha=main_sha,
        )
        with self.assertRaisesRegex(consumer.ProofError, "Timed out"):
            run_once(
                api,
                event_name="push",
                branch="release",
                head_sha=main_sha,
                source_sha=main_sha,
            )

    def test_different_pr_head_or_branch_fails_closed(self):
        api = FakeApi()
        api.listing[0]["head_sha"] = "d" * 40
        with self.assertRaisesRegex(consumer.ProofError, "Timed out"):
            run_once(api)

    def test_wrong_artifact_type_is_not_accepted(self):
        api = FakeApi()
        api.get = lambda url, binary=False: (
            {"workflow_runs": api.listing} if "/workflows/build.yml/runs?" in url
            else {"artifacts": []}
        )
        with self.assertRaisesRegex(consumer.ProofError, "exactly one"):
            run_once(api)

    def test_cross_host_redirect_strips_bearer_token(self):
        handler = consumer.StripCrossOriginAuthorization()
        request = urllib.request.Request(
            "https://api.github.com/repos/masarray/arsas/actions/artifacts/42/zip",
            headers={"Authorization": "Bearer example-token"}
        )
        redirected = handler.redirect_request(
            request, None, 302, "Found", {},
            "https://artifact.blob.core.windows.net/archive"
        )
        self.assertIsNotNone(redirected)
        self.assertIsNone(redirected.get_header("Authorization"))

    def test_untrusted_repository_rejected(self):
        with self.assertRaisesRegex(consumer.ProofError, "restricted"):
            consumer.validate_identity(
                "untrusted/arsas", HEAD, SOURCE, ENGINE, BRANCH
            )


if __name__ == "__main__":
    unittest.main()
