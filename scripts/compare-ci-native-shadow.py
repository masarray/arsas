#!/usr/bin/env python3
"""CI-P3J-C read-only PR-native COMTRADE/canonical shadow comparison.

Advisory only: this tool never authorizes dropping independent native CTest.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

_ROOT = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location(
    "arsas_native_shadow_ctest", _ROOT / "write-ci-native-ctest-proof.py"
)
assert _spec and _spec.loader
_native = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_native)

ARTIFACT = "ARSAS-native-ctest-shadow"
MAX_ZIP = 4 * 1024 * 1024
JUNIT = "ctest.xml"
PROOF = "ci-native-ctest-authority.json"
NAMES = frozenset((JUNIT, PROOF))
REPO = "masarray/arsas"


class ShadowError(RuntimeError):
    pass


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        redirected = super().redirect_request(request, fp, code, msg, headers, newurl)
        if redirected is not None:
            before = urllib.parse.urlparse(request.full_url).netloc.lower()
            after = urllib.parse.urlparse(newurl).netloc.lower()
            if before != after:
                redirected.remove_header("Authorization")
        return redirected


class GitHubReadOnly:
    def __init__(self, token: str):
        if not token:
            raise ShadowError("GITHUB_TOKEN with read-only actions scope is required")
        self.token = token

    def get(self, url: str, *, binary: bool = False):
        request = urllib.request.Request(
            url, headers={
                "Authorization": "Bearer " + self.token,
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "ARSAS-CI-P3J-C-Native-Shadow",
            }, method="GET",
        )
        try:
            with urllib.request.build_opener(SafeRedirect()).open(request, timeout=30) as result:
                limit = MAX_ZIP if binary else 1024 * 1024
                payload = result.read(limit + 1)
                if len(payload) > limit:
                    raise ShadowError("Shadow evidence download exceeds bounded size")
                return payload if binary else json.loads(payload)
        except (urllib.error.URLError, ValueError) as exc:
            raise ShadowError(f"Read-only GitHub shadow evidence request failed: {exc}") from exc


def choose_exact_run(payload: dict, *, head_sha: str, head_branch: str) -> dict | None:
    runs = payload.get("workflow_runs")
    if not isinstance(runs, list):
        raise ShadowError("Canonical run list is malformed")
    chosen = [
        run for run in runs
        if run.get("name") == "Build ARSAS"
        and run.get("event") == "pull_request"
        and run.get("head_sha") == head_sha
        and run.get("head_branch") == head_branch
        and isinstance(run.get("id"), int)
        and isinstance(run.get("run_attempt"), int)
    ]
    if not chosen:
        return None
    # Reruns and retries supersede prior proof on the same PR/head.
    return max(chosen, key=lambda run: (run["id"], run["run_attempt"]))


def one_artifact(payload: dict, run_id: int) -> dict | None:
    entries = payload.get("artifacts")
    if not isinstance(entries, list):
        raise ShadowError("Canonical artifact list is malformed")
    matches = [
        a for a in entries
        if a.get("name") == ARTIFACT
        and a.get("expired") is False
        and a.get("workflow_run", {}).get("id") == run_id
        and isinstance(a.get("id"), int)
    ]
    if len(matches) > 1:
        raise ShadowError("Duplicate canonical native CTest shadow artifacts")
    return matches[0] if matches else None


def parse_small_artifact(blob: bytes, expected_digest: str) -> tuple[bytes, bytes]:
    if not blob or len(blob) > MAX_ZIP:
        raise ShadowError("Native CTest artifact ZIP missing or oversized")
    if not isinstance(expected_digest, str) or not expected_digest.startswith("sha256:"):
        raise ShadowError("Canonical artifact SHA-256 metadata missing")
    if hashlib.sha256(blob).hexdigest() != expected_digest.removeprefix("sha256:"):
        raise ShadowError("Canonical artifact ZIP SHA-256 does not match API metadata")
    try:
        with zipfile.ZipFile(io.BytesIO(blob)) as archive:
            files = [i for i in archive.infolist() if not i.is_dir()]
            if {i.filename for i in files} != NAMES or len(files) != 2:
                raise ShadowError("Canonical native artifact has missing/extra evidence files")
            if any(i.file_size > MAX_ZIP or i.flag_bits & 0x1 for i in files):
                raise ShadowError("Canonical native artifact contains unsafe entries")
            return archive.read(JUNIT), archive.read(PROOF)
    except (zipfile.BadZipFile, RuntimeError, KeyError) as exc:
        raise ShadowError("Canonical native shadow artifact is not a readable ZIP") from exc


def compare_proofs(
    *, junit: bytes, canonical_json: bytes,
    independent_junit: Path, independent_bridge: Path,
    source_sha: str, engine_sha: str, ardirec_sha: str,
    run_id: int, run_attempt: int, event: str,
) -> dict:
    try:
        claimed = json.loads(canonical_json)
        if not isinstance(claimed, dict):
            raise ValueError("Native proof JSON must be an object")
        canonical = _native.verify_sealed_native_proof(
            proof_bytes=canonical_json, junit_bytes=junit,
            bridge_sha256=claimed.get("nativeBridgeSha256"),
            bridge_size=claimed.get("nativeBridgeSizeBytes"),
            source_sha=source_sha, engine_sha=engine_sha, ardirec_sha=ardirec_sha,
            run_id=run_id, attempt=run_attempt, event=event,
        )
        local_names = _native.parse_ctest_junit(independent_junit)
        local_sha = _native.digest(independent_bridge)
        local_size = independent_bridge.stat().st_size
    except (OSError, ValueError, KeyError, TypeError) as exc:
        raise ShadowError(f"Invalid canonical/independent native evidence: {exc}") from exc
    inventory_match = local_names == canonical["testNames"]
    bytes_match = (local_sha == canonical["nativeBridgeSha256"]
                   and local_size == canonical["nativeBridgeSizeBytes"])
    return {
        "sourceSha": source_sha,
        "engineSha": engine_sha,
        "ardirecSha": ardirec_sha,
        "canonicalRunId": run_id,
        "canonicalRunAttempt": run_attempt,
        "canonicalTestNames": canonical["testNames"],
        "independentTestNames": local_names,
        "canonicalBridgeSha256": canonical["nativeBridgeSha256"],
        "independentBridgeSha256": local_sha,
        "ctestInventoryEqual": inventory_match,
        "bridgeBytesEqual": bytes_match,
        "shadowParityObserved": inventory_match and bytes_match,
        "independentWindowsCTestRetained": True,
        "releasePromotionAuthority": False,
        "deduplicationAuthorized": False,
    }


def compare_online(
    api, *, head_sha: str, head_branch: str, source_sha: str,
    engine_sha: str, ardirec_sha: str, independent_junit: Path,
    independent_bridge: Path, wait_seconds: int = 600, poll_seconds: int = 15,
) -> dict:
    for label, sha in (("head", head_sha), ("source", source_sha),
                       ("engine", engine_sha), ("ArdIrec", ardirec_sha)):
        if not _native.SHA_RE.fullmatch(sha):
            raise ShadowError(f"Invalid exact {label} SHA")
    if not head_branch or len(head_branch) > 180 or "\n" in head_branch:
        raise ShadowError("Invalid exact PR branch")
    if wait_seconds < 0 or wait_seconds > 1200 or poll_seconds < 1:
        raise ShadowError("Invalid bounded shadow wait")
    base = "https://api.github.com/repos/" + REPO
    query = urllib.parse.urlencode({
        "event": "pull_request", "head_sha": head_sha, "per_page": 100,
    })
    deadline = time.monotonic() + wait_seconds
    last = "canonical run/artifact not ready"
    while True:
        run = choose_exact_run(
            api.get(f"{base}/actions/workflows/build.yml/runs?{query}"),
            head_sha=head_sha, head_branch=head_branch,
        )
        if run is not None:
            status, conclusion = run.get("status"), run.get("conclusion")
            last = f"run {run['id']}/{run['run_attempt']} {status}/{conclusion}"
            if status == "completed" and conclusion != "success":
                raise ShadowError("Latest matching canonical build failed: " + last)
            if status in {"in_progress", "completed"}:
                art = one_artifact(
                    api.get(f"{base}/actions/runs/{run['id']}/artifacts?per_page=100"),
                    run["id"],
                )
                if art is not None:
                    try:
                        blob = api.get(art["archive_download_url"], binary=True)
                    except ShadowError as exc:
                        if "404" not in str(exc):
                            raise
                    else:
                        j, proof = parse_small_artifact(blob, art.get("digest"))
                        result = compare_proofs(
                            junit=j, canonical_json=proof,
                            independent_junit=independent_junit,
                            independent_bridge=independent_bridge,
                            source_sha=source_sha, engine_sha=engine_sha,
                            ardirec_sha=ardirec_sha,
                            run_id=run["id"], run_attempt=run["run_attempt"],
                            event="pull_request",
                        )
                        result["canonicalRunStatus"] = status
                        result["canonicalRunConclusion"] = conclusion
                        result["fullCanonicalBuildPassed"] = (
                            status == "completed" and conclusion == "success"
                        )
                        result["provisional"] = not result["fullCanonicalBuildPassed"]
                        return result
        if time.monotonic() >= deadline:
            raise ShadowError(f"Bounded shadow comparison timeout; last={last}")
        time.sleep(min(poll_seconds, deadline - time.monotonic()))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--head-sha", required=True)
    parser.add_argument("--head-branch", required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--engine-sha", required=True)
    parser.add_argument("--ardirec-sha", required=True)
    parser.add_argument("--independent-junit", type=Path, required=True)
    parser.add_argument("--independent-bridge", type=Path, required=True)
    parser.add_argument("--wait-seconds", type=int, default=600)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = compare_online(
        GitHubReadOnly(os.environ.get("GITHUB_TOKEN", "")),
        head_sha=args.head_sha, head_branch=args.head_branch,
        source_sha=args.source_sha, engine_sha=args.engine_sha,
        ardirec_sha=args.ardirec_sha,
        independent_junit=args.independent_junit,
        independent_bridge=args.independent_bridge,
        wait_seconds=args.wait_seconds,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, sort_keys=True, indent=2) + "\n",
                           encoding="utf-8")
    print("CI-P3J-C shadow result: " + json.dumps(result, sort_keys=True))
    if not result["shadowParityObserved"]:
        raise ShadowError("Independent native build differs; keep Windows COMTRADE CTest")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
