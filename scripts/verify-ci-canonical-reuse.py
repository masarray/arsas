#!/usr/bin/env python3
"""CI-P2: reuse canonical Build ARSAS regression proof, fail closed.

This proof reader runs on a read-only GitHub token. Strict mode requires the
canonical Build ARSAS workflow to complete successfully. CI-P2D adds an explicit
artifact-ready mode that can prove the full regression earlier, after the exact
manifest/TRX artifact is uploaded, while portable packaging/smoke remains a
separate Build ARSAS responsibility.

Downloaded artifacts are treated strictly as untrusted ZIP *data*. No file
from the artifact is extracted or executed.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import PurePosixPath

import importlib.util
from pathlib import Path

_source = Path(__file__).with_name("write-ci-canonical-proof.py")
_spec = importlib.util.spec_from_file_location("arsas_ci_canonical_proof", _source)
assert _spec and _spec.loader
_module = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_module)
counters_from_trx = _module.counters_from_trx
sha = _module.sha

MAX_ARCHIVE_BYTES = 50 * 1024 * 1024
MAX_EVIDENCE_BYTES = 12 * 1024 * 1024
EXPECTED_ARTIFACT = "ARSAS-test-evidence"
EXPECTED_MANIFEST = "ci-canonical-authority.json"
EXPECTED_TRX = "arsas-tests.trx"


class ProofError(RuntimeError):
    pass


def validate_identity(repo: str, head_sha: str, merge_sha: str, engine_sha: str,
                      branch: str, event_name: str = "pull_request") -> None:
    if repo != "masarray/arsas":
        raise ProofError("Canonical CI reuse is restricted to masarray/arsas")
    for label, value in [
        ("PR head", head_sha), ("PR merge", merge_sha), ("engine", engine_sha)
    ]:
        try:
            sha(value, label)
        except ValueError as exc:
            raise ProofError(str(exc)) from exc
    if not branch or len(branch) > 200 or "\n" in branch:
        raise ProofError("Invalid canonical branch identity")
    if event_name not in {"pull_request", "push"}:
        raise ProofError("Unsupported canonical workflow event")


def candidate_runs(
    payload: dict, head_sha: str, branch: str, event_name: str = "pull_request"
) -> list[dict]:
    runs = payload.get("workflow_runs", [])
    if not isinstance(runs, list):
        raise ProofError("GitHub Actions run listing is malformed")
    matches = [
        run for run in runs
        if run.get("event") == event_name
        and run.get("name") == "Build ARSAS"
        and run.get("head_sha") == head_sha
        and run.get("head_branch") == branch
        and isinstance(run.get("id"), int)
        and isinstance(run.get("run_attempt"), int)
    ]
    return sorted(matches, key=lambda run: (run["id"], run["run_attempt"]), reverse=True)


def validate_artifact_archive(
    payload: bytes, *, merge_sha: str, engine_sha: str,
    workflow_run_id: int, run_attempt: int,
    expected_event_name: str = "pull_request",
) -> dict:
    if not payload or len(payload) > MAX_ARCHIVE_BYTES:
        raise ProofError("Canonical artifact is missing or exceeds bounded size")

    try:
        archive = zipfile.ZipFile(io.BytesIO(payload))
    except (zipfile.BadZipFile, OSError) as exc:
        raise ProofError("Canonical test evidence is not a valid ZIP archive") from exc

    with archive:
        entries = {}
        for info in archive.infolist():
            if info.is_dir():
                continue
            name = info.filename.replace("\\", "/")
            path = PurePosixPath(name)
            if (
                path.is_absolute()
                or ".." in path.parts
                or ":" in name
                or info.file_size < 0
                or info.file_size > MAX_EVIDENCE_BYTES
            ):
                raise ProofError("Canonical evidence archive has an unsafe or oversized entry")
            basename = path.name
            if basename in (EXPECTED_MANIFEST, EXPECTED_TRX):
                if basename in entries:
                    raise ProofError(f"Canonical artifact contains duplicate {basename}")
                entries[basename] = info

        if set(entries) != {EXPECTED_MANIFEST, EXPECTED_TRX}:
            raise ProofError("Canonical manifest/TRX evidence is missing from artifact")

        try:
            manifest = json.loads(archive.read(entries[EXPECTED_MANIFEST]))
            trx = archive.read(entries[EXPECTED_TRX])
        except (ValueError, KeyError, RuntimeError, OSError) as exc:
            raise ProofError("Canonical archive proof cannot be decoded safely") from exc

    if manifest.get("schemaVersion") != 1:
        raise ProofError("Canonical proof schema version changed unexpectedly")
    if manifest.get("kind") != "arsas-canonical-regression-evidence":
        raise ProofError("Wrong canonical artifact provenance kind")
    if manifest.get("canonicalWorkflow") != "Build ARSAS":
        raise ProofError("Proof is not from canonical Build ARSAS")
    if manifest.get("eventName") != expected_event_name:
        raise ProofError(
            f"Canonical proof event mismatch: expected {expected_event_name}"
        )
    if manifest.get("workflowRunId") != workflow_run_id:
        raise ProofError("Canonical workflow run ID differs from API run")
    if manifest.get("runAttempt") != run_attempt:
        raise ProofError("Canonical run attempt differs from API run attempt")
    if manifest.get("exactSourceSha") != merge_sha:
        raise ProofError("Canonical test proof is for a stale/different source revision")
    if manifest.get("exactEngineSha") != engine_sha:
        raise ProofError("Canonical engine SHA differs from this PR's immutable lock")
    if manifest.get("fullRegressionPassed") is not True:
        raise ProofError("Canonical test evidence does not prove full test PASS")
    if manifest.get("releasePromotionAuthority") is not False:
        raise ProofError("Canonical regression proof misrepresents release authority")
    if manifest.get("testFile") != EXPECTED_TRX:
        raise ProofError("Canonical proof references unexpected TRX filename")
    if manifest.get("testTrxSha256") != hashlib.sha256(trx).hexdigest():
        raise ProofError("Canonical TRX digest differs from recorded proof")
    try:
        actual = counters_from_trx(trx)
    except (ValueError, ArithmeticError) as exc:
        raise ProofError("TRX does not prove a fully passing test suite") from exc
    if manifest.get("testCounters") != actual:
        raise ProofError("Canonical TRX counters disagree with manifest")
    return {
        "canonicalRunId": workflow_run_id,
        "canonicalRunAttempt": run_attempt,
        "mergeSha": merge_sha,
        "engineSha": engine_sha,
        "passed": actual["passed"],
        "failed": actual["failed"],
        "notExecuted": actual["notExecuted"],
        "artifactSha256": hashlib.sha256(payload).hexdigest(),
    }


def choose_artifact(payload: dict, run_id: int, *, allow_missing: bool = False) -> dict | None:
    artifacts = payload.get("artifacts", [])
    if not isinstance(artifacts, list):
        raise ProofError("Canonical artifact listing is malformed")
    matches = [
        obj for obj in artifacts if obj.get("name") == EXPECTED_ARTIFACT
        and obj.get("expired") is False and isinstance(obj.get("id"), int)
        and obj.get("workflow_run", {}).get("id", run_id) == run_id
    ]
    if not matches and allow_missing:
        return None
    if len(matches) != 1:
        raise ProofError("Expected exactly one non-expired canonical test artifact")
    return matches[0]


class StripCrossOriginAuthorization(urllib.request.HTTPRedirectHandler):
    """Do not forward GitHub bearer credentials to artifact blob hosts."""

    def redirect_request(self, request, fp, code, msg, headers, newurl):
        redirected = super().redirect_request(
            request, fp, code, msg, headers, newurl
        )
        if redirected is not None:
            before = urllib.parse.urlparse(request.full_url).netloc.lower()
            after = urllib.parse.urlparse(newurl).netloc.lower()
            if before != after:
                redirected.remove_header("Authorization")
        return redirected


class GitHubReadOnly:
    def __init__(self, token: str):
        if not token:
            raise ProofError("A read-only GITHUB_TOKEN is required")
        self.token = token

    def get(self, url: str, binary: bool = False):
        req = urllib.request.Request(
            url,
            headers={
                "Authorization": f"Bearer {self.token}",
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "ARSAS-CI-P2-Canonical-Reuse",
            },
            method="GET",
        )
        try:
            opener = urllib.request.build_opener(StripCrossOriginAuthorization())
            with opener.open(req, timeout=30) as reply:
                data = reply.read(MAX_ARCHIVE_BYTES + 1 if binary else 3 * 1024 * 1024)
                if binary:
                    return data
                return json.loads(data)
        except (urllib.error.HTTPError, urllib.error.URLError, ValueError) as exc:
            raise ProofError(f"GitHub read-only evidence request failed: {exc}") from exc


def verify_canonical(
    api: GitHubReadOnly, *, repository: str, branch: str,
    head_sha: str, merge_sha: str, engine_sha: str,
    wait_seconds: int = 960, poll_seconds: int = 10,
    allow_in_progress_artifact: bool = False,
    event_name: str = "pull_request",
) -> dict:
    validate_identity(
        repository, head_sha, merge_sha, engine_sha, branch, event_name
    )
    if wait_seconds < 0 or poll_seconds < 1:
        raise ProofError("Invalid bounded wait policy")

    base = "https://api.github.com/repos/" + repository
    query = urllib.parse.urlencode(
        {"event": event_name, "head_sha": head_sha, "per_page": 100}
    )
    url = f"{base}/actions/workflows/build.yml/runs?{query}"
    deadline = time.monotonic() + wait_seconds
    previous = None

    while True:
        runs = candidate_runs(api.get(url), head_sha, branch, event_name)
        if runs:
            run = runs[0]  # Never use an older successful attempt over a newer failure.
            run_id, attempt = run["id"], run["run_attempt"]
            status = run.get("status")
            conclusion = run.get("conclusion")
            previous = f"{run_id}/{attempt} {status}/{conclusion}"

            # A canonical run that has already finished red is never usable, even
            # if an all-pass regression artifact was uploaded before a later
            # packaging/smoke failure.
            if status == "completed" and conclusion != "success":
                raise ProofError(
                    f"Latest canonical Build ARSAS run failed: {previous}"
                )

            can_read_artifact = (
                status == "completed"
                or (allow_in_progress_artifact and status == "in_progress")
            )
            if can_read_artifact:
                artifact = choose_artifact(
                    api.get(f"{base}/actions/runs/{run_id}/artifacts?per_page=100"),
                    run_id,
                    allow_missing=(status != "completed"),
                )
                if artifact is not None:
                    blob = api.get(artifact["archive_download_url"], binary=True)
                    try:
                        proof = validate_artifact_archive(
                            blob, merge_sha=merge_sha, engine_sha=engine_sha,
                            workflow_run_id=run_id, run_attempt=attempt,
                            expected_event_name=event_name,
                        )
                    except ProofError as exc:
                        if "stale/different PR merge tree" not in str(exc):
                            raise
                        # A stale green artifact is not authoritative for the current
                        # synthetic merge tree; a replacement run can still arrive.
                        previous = f"{previous} (stale merge tree)"
                    else:
                        proof.update({
                            "canonicalRunStatus": status,
                            "canonicalRunConclusion": conclusion,
                            "workflowCompleted": status == "completed",
                            "proofStage": (
                                "completed-workflow"
                                if status == "completed"
                                else "full-regression-artifact-ready"
                            ),
                            "packagingSmokeProven": status == "completed",
                            "eventName": event_name,
                            "branch": branch,
                        })
                        return proof

        if time.monotonic() >= deadline:
            mode = (
                "artifact-ready canonical Build ARSAS evidence"
                if allow_in_progress_artifact
                else "completed canonical Build ARSAS evidence"
            )
            raise ProofError(
                f"Timed out waiting for {mode} "
                f"for head={head_sha}, merge={merge_sha}, last={previous}"
            )
        time.sleep(min(poll_seconds, max(0.0, deadline - time.monotonic())))

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--head-branch", required=True)
    parser.add_argument("--head-sha", required=True)
    parser.add_argument("--merge-sha", required=True)
    parser.add_argument("--engine-sha", required=True)
    parser.add_argument(
        "--event-name",
        choices=("pull_request", "push"),
        default="pull_request",
    )
    parser.add_argument("--wait-seconds", type=int, default=960)
    parser.add_argument(
        "--allow-in-progress-artifact",
        action="store_true",
        help=(
            "Accept exact all-pass regression manifest/TRX from the latest matching "
            "in-progress Build ARSAS run. Does not prove later packaging/smoke."
        ),
    )
    parser.add_argument("--output", default="")
    args = parser.parse_args()
    result = verify_canonical(
        GitHubReadOnly(os.environ.get("GITHUB_TOKEN", "")),
        repository=args.repository,
        branch=args.head_branch,
        head_sha=args.head_sha,
        merge_sha=args.merge_sha,
        engine_sha=args.engine_sha,
        wait_seconds=args.wait_seconds,
        allow_in_progress_artifact=args.allow_in_progress_artifact,
        event_name=args.event_name,
    )
    payload = json.dumps(result, sort_keys=True)
    print("Canonical full-regression reuse PASS: " + payload)
    if args.output:
        with open(args.output, "w", encoding="utf-8") as stream:
            stream.write(payload + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
