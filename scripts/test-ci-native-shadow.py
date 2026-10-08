#!/usr/bin/env python3
"""Offline fail-closed and advisory CI-P3J-C native shadow fixtures."""
from __future__ import annotations

import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location(
    "arsas_shadow", ROOT / "compare-ci-native-shadow.py"
)
assert spec and spec.loader
shadow = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shadow)
spec2 = importlib.util.spec_from_file_location(
    "arsas_native_proof_test", ROOT / "write-ci-native-ctest-proof.py"
)
assert spec2 and spec2.loader
native = importlib.util.module_from_spec(spec2)
spec2.loader.exec_module(native)

HEAD, MERGE, ENGINE, ARDIREC = "d" * 40, "a" * 40, "b" * 40, "c" * 40
BRANCH = "ci/p3j-c-native-shadow"
RUN = 1234
XML = b'<testsuite tests="2" failures="0" errors="0" skipped="0"><testcase name="ABI"/><testcase name="Locus"/></testsuite>'


def artifact_bytes(*, source=MERGE, bridge=b"canonical-tested-binary", names=XML,
                   run_id=RUN, event="pull_request") -> tuple[bytes, str]:
    proof = native.record_from_bytes(
        junit_bytes=names,
        bridge_sha256=hashlib.sha256(bridge).hexdigest(),
        bridge_size=len(bridge),
        source_sha=source, engine_sha=ENGINE, ardirec_sha=ARDIREC,
        run_id=run_id, attempt=1, event=event,
    )
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as f:
        f.writestr(shadow.JUNIT, names)
        f.writestr(shadow.PROOF, json.dumps(proof).encode())
    return output.getvalue(), hashlib.sha256(output.getvalue()).hexdigest()


class FakeApi:
    def __init__(self, *, blob, digest, run_status="completed", run_conclusion="success",
                 head=HEAD, source_branch=BRANCH, artifact=True, duplicate=False):
        self.blob = blob
        self.digest = digest
        self.run_status = run_status
        self.run_conclusion = run_conclusion
        self.head = head
        self.branch = source_branch
        self.artifact = artifact
        self.duplicate = duplicate

    def get(self, url: str, *, binary=False):
        if binary:
            return self.blob
        if "/workflows/build.yml/runs?" in url:
            return {"workflow_runs": [{
                "id": RUN, "run_attempt": 1, "name": "Build ARSAS",
                "head_sha": self.head, "head_branch": self.branch,
                "event": "pull_request", "status": self.run_status,
                "conclusion": self.run_conclusion,
            }]}
        if "/artifacts?" in url:
            entries = []
            if self.artifact:
                entries = [{
                    "id": 77, "name": shadow.ARTIFACT,
                    "expired": False,
                    "workflow_run": {"id": RUN},
                    "digest": "sha256:" + self.digest,
                    "archive_download_url": "https://example.invalid/artifact.zip",
                }]
            if self.duplicate:
                entries.append(dict(entries[0], id=78))
            return {"artifacts": entries}
        raise RuntimeError("Unexpected fake API request: " + url)


class NativeShadowTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.local = self.root / "local-ctest.xml"
        self.bridge = self.root / "native.dll"
        self.local.write_bytes(XML)
        self.bridge.write_bytes(b"canonical-tested-binary")
        self.blob, self.digest = artifact_bytes()
        self.kwargs = {
            "head_sha": HEAD, "head_branch": BRANCH,
            "source_sha": MERGE, "engine_sha": ENGINE, "ardirec_sha": ARDIREC,
            "independent_junit": self.local, "independent_bridge": self.bridge,
            "wait_seconds": 0,
        }

    def tearDown(self):
        self.tmp.cleanup()

    def test_same_exact_merge_and_native_bits_parity(self):
        result = shadow.compare_online(
            FakeApi(blob=self.blob, digest=self.digest), **self.kwargs
        )
        self.assertTrue(result["shadowParityObserved"])
        self.assertTrue(result["fullCanonicalBuildPassed"])
        self.assertFalse(result["deduplicationAuthorized"])
        self.assertFalse(result["releasePromotionAuthority"])
        self.assertEqual(result["canonicalRunId"], RUN)

    def test_native_dll_mismatch_is_visible_not_approved(self):
        self.bridge.write_bytes(b"non-identical-native-dll")
        result = shadow.compare_online(
            FakeApi(blob=self.blob, digest=self.digest), **self.kwargs
        )
        self.assertFalse(result["shadowParityObserved"])
        self.assertFalse(result["bridgeBytesEqual"])

    def test_native_test_inventory_mismatch_is_visible(self):
        self.local.write_bytes(XML.replace(b"Locus", b"Other"))
        result = shadow.compare_online(
            FakeApi(blob=self.blob, digest=self.digest), **self.kwargs
        )
        self.assertFalse(result["shadowParityObserved"])
        self.assertFalse(result["ctestInventoryEqual"])

    def test_wrong_exact_source_or_engine_is_rejected(self):
        for edits in ({"source_sha": "f" * 40},
                      {"engine_sha": "f" * 40},
                      {"ardirec_sha": "f" * 40}):
            with self.subTest(edits=edits):
                kw = dict(self.kwargs, **edits)
                with self.assertRaisesRegex(shadow.ShadowError, "evidence"):
                    shadow.compare_online(
                        FakeApi(blob=self.blob, digest=self.digest), **kw
                    )

    def test_wrong_head_sha_is_not_matched_to_old_run(self):
        kw = dict(self.kwargs, head_sha="f" * 40)
        with self.assertRaisesRegex(shadow.ShadowError, "timeout"):
            shadow.compare_online(
                FakeApi(blob=self.blob, digest=self.digest), **kw
            )

    def test_rejected_failed_latest_canonical_run(self):
        with self.assertRaisesRegex(shadow.ShadowError, "Latest matching"):
            shadow.compare_online(
                FakeApi(blob=self.blob, digest=self.digest,
                        run_status="completed", run_conclusion="failure"),
                **self.kwargs,
            )

    def test_in_progress_artifact_is_provisional_not_promotion(self):
        result = shadow.compare_online(
            FakeApi(blob=self.blob, digest=self.digest,
                    run_status="in_progress", run_conclusion=None),
            **self.kwargs,
        )
        self.assertTrue(result["provisional"])
        self.assertFalse(result["fullCanonicalBuildPassed"])
        self.assertFalse(result["deduplicationAuthorized"])

    def test_missing_digest_and_modified_archive_are_rejected(self):
        with self.assertRaisesRegex(shadow.ShadowError, "metadata missing"):
            shadow.parse_small_artifact(self.blob, "")
        with self.assertRaisesRegex(shadow.ShadowError, "SHA-256"):
            shadow.parse_small_artifact(self.blob, "sha256:" + "0" * 64)

    def test_extra_file_or_duplicate_artifact_is_rejected(self):
        with zipfile.ZipFile(io.BytesIO(self.blob)) as source:
            data = {x: source.read(x) for x in source.namelist()}
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as dest:
            for name, value in data.items():
                dest.writestr(name, value)
            dest.writestr("payload.exe", b"unexpected")
        payload = buffer.getvalue()
        with self.assertRaisesRegex(shadow.ShadowError, "extra evidence"):
            shadow.parse_small_artifact(
                payload, "sha256:" + hashlib.sha256(payload).hexdigest()
            )
        with self.assertRaisesRegex(shadow.ShadowError, "Duplicate"):
            shadow.compare_online(
                FakeApi(blob=self.blob, digest=self.digest, duplicate=True),
                **self.kwargs,
            )

    def test_ci_preserves_native_independent_ctest_and_advisory_only(self):
        comtrade = (ROOT.parent / ".github/workflows/comtrade-viewer-integration.yml").read_text()
        canonical = (ROOT.parent / ".github/workflows/build.yml").read_text()
        for token in ("-CTestJunitPath", "compare-ci-native-shadow.py",
                      "continue-on-error: true", "Enforce field-tested native-only"):
            self.assertIn(token, comtrade)
        self.assertIn("ARSAS-native-ctest-shadow", canonical)
        self.assertIn("actions/upload-artifact@v7", canonical)
        self.assertIn("Build and test pinned native bridge", comtrade)
        self.assertIn("ARIEC61850_COMMIT=$($engineLock.commit)", comtrade)
        self.assertIn("engines\\ARIEC61850.lock.json", comtrade)


if __name__ == "__main__":
    unittest.main()
