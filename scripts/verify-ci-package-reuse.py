#!/usr/bin/env python3
"""Verify and safely materialize the exact canonical Windows package artifact."""
from __future__ import annotations

import argparse
import hashlib
import io
import importlib.util
import json
import os
import shutil
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath

_here = Path(__file__).resolve().parent

_spec = importlib.util.spec_from_file_location(
    "arsas_ci_package_producer", _here / "write-ci-package-manifest.py"
)
assert _spec and _spec.loader
_producer = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_producer)
MANIFEST_NAME = _producer.MANIFEST_NAME
KIND = _producer.KIND
CANONICAL_MANIFEST = _producer.CANONICAL_MANIFEST
CANONICAL_TRX = _producer.CANONICAL_TRX
counters_from_trx = _producer.counters_from_trx
sha = _producer.sha

EXPECTED_ARTIFACT = "ARSAS-windows-package-input"
MAX_ARCHIVE_BYTES = 768 * 1024 * 1024
MAX_FILE_BYTES = 256 * 1024 * 1024
MAX_TOTAL_BYTES = 1024 * 1024 * 1024
MAX_FILES = 5000


class PackageProofError(RuntimeError):
    pass


class GitHubRequestError(PackageProofError):
    """Typed read-only GitHub request failure used for bounded retry decisions."""

    def __init__(self, message: str, *, status: int | None = None):
        super().__init__(message)
        self.status = status


def validate_identity(
    repository: str, branch: str, head_sha: str, source_sha: str,
    engine_sha: str, ardirec_sha: str, event_name: str
) -> None:
    if repository != "masarray/arsas":
        raise PackageProofError("Canonical package reuse is restricted to masarray/arsas")
    for label, value in (
        ("head", head_sha),
        ("source", source_sha),
        ("engine", engine_sha),
        ("ArdIrec", ardirec_sha),
    ):
        try:
            sha(value, label)
        except ValueError as exc:
            raise PackageProofError(str(exc)) from exc
    if not branch or len(branch) > 200 or "\n" in branch:
        raise PackageProofError("Invalid canonical branch identity")
    if event_name not in {"pull_request", "push"}:
        raise PackageProofError("Unsupported canonical package event")


def candidate_runs(payload: dict, head_sha: str, branch: str, event_name: str) -> list[dict]:
    runs = payload.get("workflow_runs", [])
    if not isinstance(runs, list):
        raise PackageProofError("Build ARSAS run listing is malformed")
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


def choose_artifact(payload: dict, run_id: int, *, allow_missing: bool) -> dict | None:
    artifacts = payload.get("artifacts", [])
    if not isinstance(artifacts, list):
        raise PackageProofError("Canonical package artifact listing is malformed")
    matches = [
        obj for obj in artifacts
        if obj.get("name") == EXPECTED_ARTIFACT
        and obj.get("expired") is False
        and isinstance(obj.get("id"), int)
        and obj.get("workflow_run", {}).get("id") == run_id
    ]
    if not matches and allow_missing:
        return None
    if len(matches) != 1:
        raise PackageProofError("Expected exactly one non-expired canonical package artifact")
    return matches[0]


def _zip_relative(info: zipfile.ZipInfo, prefix: PurePosixPath) -> str:
    name = info.filename.replace("\\", "/")
    path = PurePosixPath(name)
    if path.is_absolute() or ".." in path.parts or ":" in name:
        raise PackageProofError("Canonical package archive contains an unsafe path")
    try:
        rel = path.relative_to(prefix) if str(prefix) != "." else path
    except ValueError as exc:
        raise PackageProofError("Canonical package archive contains files outside its root") from exc
    rel_text = rel.as_posix()
    if not rel_text or rel_text == ".":
        raise PackageProofError("Canonical package archive contains an invalid root entry")
    return rel_text


WINDOWS_RESERVED_NAMES = {
    "con", "prn", "aux", "nul",
    *(f"com{i}" for i in range(1, 10)),
    *(f"lpt{i}" for i in range(1, 10)),
}


def _windows_path_key(path_text: str) -> str:
    """Return a Windows-canonical collision key or reject unsafe aliases."""
    path = PurePosixPath(path_text)
    normalized = []
    for part in path.parts:
        if (
            not part
            or part in {".", ".."}
            or part.endswith((" ", "."))
            or any(ord(ch) < 32 for ch in part)
        ):
            raise PackageProofError(
                "Canonical package contains a Windows-unsafe path component"
            )
        device = part.split(".", 1)[0].casefold()
        if device in WINDOWS_RESERVED_NAMES:
            raise PackageProofError(
                "Canonical package contains a reserved Windows device path"
            )
        normalized.append(part.casefold())
    if not normalized:
        raise PackageProofError("Canonical package contains an empty Windows path")
    return "/".join(normalized)


def _is_symlink(info: zipfile.ZipInfo) -> bool:
    return ((info.external_attr >> 16) & 0o170000) == 0o120000


def _hash_zip_entry(archive: zipfile.ZipFile, info: zipfile.ZipInfo) -> str:
    h = hashlib.sha256()
    with archive.open(info, "r") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def validate_package_archive(
    payload: bytes, *, source_sha: str, engine_sha: str, ardirec_sha: str,
    workflow_run_id: int, run_attempt: int, event_name: str,
    output_dir: Path | None = None
) -> dict:
    if not payload or len(payload) > MAX_ARCHIVE_BYTES:
        raise PackageProofError("Canonical package artifact is missing or exceeds bounded size")
    try:
        archive = zipfile.ZipFile(io.BytesIO(payload))
    except (zipfile.BadZipFile, OSError) as exc:
        raise PackageProofError("Canonical package artifact is not a valid ZIP") from exc

    with archive:
        raw_files = [info for info in archive.infolist() if not info.is_dir()]
        if not raw_files or len(raw_files) > MAX_FILES:
            raise PackageProofError("Canonical package archive file count is invalid")
        manifest_infos = [
            info for info in raw_files
            if PurePosixPath(info.filename.replace("\\", "/")).name == MANIFEST_NAME
        ]
        if len(manifest_infos) != 1:
            raise PackageProofError("Canonical package manifest is missing or duplicated")
        manifest_info = manifest_infos[0]
        prefix = PurePosixPath(manifest_info.filename.replace("\\", "/")).parent

        infos: dict[str, zipfile.ZipInfo] = {}
        windows_archive_paths: dict[str, str] = {}
        total_uncompressed = 0
        for info in raw_files:
            if info.flag_bits & 0x1 or _is_symlink(info):
                raise PackageProofError("Encrypted files and symlinks are not permitted")
            if info.file_size < 0 or info.file_size > MAX_FILE_BYTES:
                raise PackageProofError("Canonical package contains an oversized file")
            total_uncompressed += info.file_size
            if total_uncompressed > MAX_TOTAL_BYTES:
                raise PackageProofError("Canonical package exceeds bounded uncompressed size")
            rel = _zip_relative(info, prefix)
            if rel in infos:
                raise PackageProofError("Canonical package contains duplicate normalized paths")
            windows_key = _windows_path_key(rel)
            previous = windows_archive_paths.get(windows_key)
            if previous is not None and previous != rel:
                raise PackageProofError(
                    "Canonical package contains a Windows-normalized path collision"
                )
            windows_archive_paths[windows_key] = rel
            infos[rel] = info

        if MANIFEST_NAME not in infos:
            raise PackageProofError("Canonical package manifest must be rooted in artifact")

        try:
            manifest = json.loads(archive.read(infos[MANIFEST_NAME]))
        except (ValueError, KeyError, OSError, RuntimeError) as exc:
            raise PackageProofError("Canonical package manifest cannot be decoded") from exc

        if (
            manifest.get("schemaVersion") != 1
            or manifest.get("kind") != KIND
            or manifest.get("canonicalWorkflow") != "Build ARSAS"
            or manifest.get("workflowRunId") != workflow_run_id
            or manifest.get("runAttempt") != run_attempt
            or manifest.get("eventName") != event_name
            or manifest.get("sourceSha") != source_sha
            or manifest.get("engineSha") != engine_sha
            or manifest.get("ardirecSha") != ardirec_sha
            or manifest.get("runtime") != "win-x64"
            or manifest.get("fullRegressionPassed") is not True
            or manifest.get("nativeBridgeIntegrationPassed") is not True
            or manifest.get("portableSmokePassed") is not True
            or manifest.get("installerBuilt") is not False
            or manifest.get("releasePromotionAuthority") is not False
        ):
            raise PackageProofError("Canonical package manifest identity or authority is invalid")

        records = manifest.get("files")
        if not isinstance(records, list) or not records or len(records) > MAX_FILES:
            raise PackageProofError("Canonical package file manifest is malformed")
        recorded: dict[str, dict] = {}
        windows_record_paths: dict[str, str] = {}
        for record in records:
            if not isinstance(record, dict):
                raise PackageProofError("Canonical package file record is malformed")
            path = record.get("path")
            if not isinstance(path, str) or not path:
                raise PackageProofError("Canonical package file path is malformed")
            parsed = PurePosixPath(path)
            if parsed.is_absolute() or ".." in parsed.parts or ":" in path or path in recorded:
                raise PackageProofError("Canonical package file manifest has unsafe/duplicate paths")
            windows_key = _windows_path_key(path)
            previous = windows_record_paths.get(windows_key)
            if previous is not None and previous != path:
                raise PackageProofError(
                    "Canonical package file manifest has a Windows-normalized path collision"
                )
            windows_record_paths[windows_key] = path
            if not isinstance(record.get("size"), int) or record["size"] < 0:
                raise PackageProofError("Canonical package file size is invalid")
            digest = record.get("sha256")
            if not isinstance(digest, str) or len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
                raise PackageProofError("Canonical package file digest is invalid")
            recorded[path] = record

        archive_payload_paths = set(infos) - {MANIFEST_NAME}
        if set(recorded) != archive_payload_paths:
            raise PackageProofError("Canonical package archive and file manifest disagree")
        if manifest.get("fileCount") != len(recorded):
            raise PackageProofError("Canonical package file count disagrees with manifest")
        if manifest.get("totalBytes") != sum(r["size"] for r in records):
            raise PackageProofError("Canonical package byte count disagrees with manifest")

        for path, record in recorded.items():
            info = infos[path]
            if info.file_size != record["size"]:
                raise PackageProofError(f"Canonical package file size differs: {path}")
            if _hash_zip_entry(archive, info) != record["sha256"]:
                raise PackageProofError(f"Canonical package file digest differs: {path}")

        canonical_manifest_path = f"evidence/{CANONICAL_MANIFEST}"
        trx_path = f"evidence/{CANONICAL_TRX}"
        if canonical_manifest_path not in infos or trx_path not in infos:
            raise PackageProofError("Canonical regression evidence is absent from package")
        canonical_bytes = archive.read(infos[canonical_manifest_path])
        trx = archive.read(infos[trx_path])
        try:
            canonical = json.loads(canonical_bytes)
            counters = counters_from_trx(trx)
        except (ValueError, ArithmeticError) as exc:
            raise PackageProofError("Canonical regression evidence is invalid") from exc
        if (
            canonical.get("schemaVersion") != 1
            or canonical.get("kind") != "arsas-canonical-regression-evidence"
            or canonical.get("canonicalWorkflow") != "Build ARSAS"
            or canonical.get("workflowRunId") != workflow_run_id
            or canonical.get("runAttempt") != run_attempt
            or canonical.get("eventName") != event_name
            or canonical.get("exactSourceSha") != source_sha
            or canonical.get("exactEngineSha") != engine_sha
            or canonical.get("fullRegressionPassed") is not True
            or canonical.get("releasePromotionAuthority") is not False
            or canonical.get("testTrxSha256") != hashlib.sha256(trx).hexdigest()
            or canonical.get("testCounters") != counters
            or manifest.get("testCounters") != counters
            or manifest.get("testTrxSha256") != hashlib.sha256(trx).hexdigest()
            or manifest.get("canonicalTestManifestSha256") != hashlib.sha256(canonical_bytes).hexdigest()
        ):
            raise PackageProofError("Canonical regression proof does not match package manifest")

        for key in ("portable", "nativeBridge"):
            record = manifest.get(key)
            if not isinstance(record, dict) or recorded.get(record.get("path")) != record:
                raise PackageProofError(f"Canonical package {key} record is invalid")
        verification = manifest.get("verification")
        if not isinstance(verification, dict):
            raise PackageProofError("Canonical package verification references are missing")
        for key in ("testAssemblyPath", "comtradeFixturePath", "locusFixturePath"):
            path = verification.get(key)
            if not isinstance(path, str) or path not in recorded:
                raise PackageProofError(f"Canonical package verification path is invalid: {key}")

        if output_dir is not None:
            if output_dir.exists():
                shutil.rmtree(output_dir)
            output_dir.mkdir(parents=True)
            for rel, info in infos.items():
                target = output_dir / PurePosixPath(rel)
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(info, "r") as src, target.open("wb") as dst:
                    shutil.copyfileobj(src, dst, 1024 * 1024)

    return {
        "canonicalRunId": workflow_run_id,
        "canonicalRunAttempt": run_attempt,
        "sourceSha": source_sha,
        "engineSha": engine_sha,
        "ardirecSha": ardirec_sha,
        "version": manifest["version"],
        "passed": manifest["testCounters"]["passed"],
        "failed": manifest["testCounters"]["failed"],
        "notExecuted": manifest["testCounters"]["notExecuted"],
        "fileCount": manifest["fileCount"],
        "totalBytes": manifest["totalBytes"],
        "portableSha256": manifest["portable"]["sha256"],
        "nativeBridgeSha256": manifest["nativeBridge"]["sha256"],
        "installerInputRoot": manifest["installerInputRoot"],
        "testAssemblyPath": manifest["verification"]["testAssemblyPath"],
        "comtradeFixturePath": manifest["verification"]["comtradeFixturePath"],
        "locusFixturePath": manifest["verification"]["locusFixturePath"],
        "artifactSha256": hashlib.sha256(payload).hexdigest(),
    }


class StripCrossOriginAuthorization(urllib.request.HTTPRedirectHandler):
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
            raise PackageProofError("A read-only GITHUB_TOKEN is required")
        self.token = token

    def get(self, url: str, *, binary: bool = False):
        req = urllib.request.Request(
            url,
            headers={
                "Authorization": f"Bearer {self.token}",
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "ARSAS-CI-P3-Package-Reuse",
            },
            method="GET",
        )
        try:
            opener = urllib.request.build_opener(StripCrossOriginAuthorization())
            with opener.open(req, timeout=60) as reply:
                limit = MAX_ARCHIVE_BYTES + 1 if binary else 4 * 1024 * 1024
                data = reply.read(limit)
                if len(data) >= limit:
                    raise PackageProofError("GitHub package response exceeds bounded size")
                return data if binary else json.loads(data)
        except PackageProofError:
            raise
        except urllib.error.HTTPError as exc:
            raise GitHubRequestError(
                f"GitHub package evidence request failed: HTTP {exc.code} {exc.reason}",
                status=exc.code,
            ) from exc
        except (urllib.error.URLError, ValueError) as exc:
            raise GitHubRequestError(
                f"GitHub package evidence request failed: {exc}"
            ) from exc


def verify_canonical_package(
    api: GitHubReadOnly, *, repository: str, branch: str,
    head_sha: str, source_sha: str, engine_sha: str, ardirec_sha: str,
    event_name: str = "pull_request", wait_seconds: int = 1200,
    poll_seconds: int = 10, allow_in_progress_artifact: bool = False,
    output_dir: Path | None = None
) -> dict:
    validate_identity(
        repository, branch, head_sha, source_sha, engine_sha, ardirec_sha, event_name
    )
    if wait_seconds < 0 or poll_seconds < 1:
        raise PackageProofError("Invalid bounded wait policy")

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
            run = runs[0]
            run_id, attempt = run["id"], run["run_attempt"]
            status, conclusion = run.get("status"), run.get("conclusion")
            previous = f"{run_id}/{attempt} {status}/{conclusion}"
            if status == "completed" and conclusion != "success":
                raise PackageProofError(f"Latest canonical Build ARSAS run failed: {previous}")

            can_read = status == "completed" or (
                allow_in_progress_artifact and status == "in_progress"
            )
            if can_read:
                artifact = choose_artifact(
                    api.get(f"{base}/actions/runs/{run_id}/artifacts?per_page=100"),
                    run_id,
                    allow_missing=(status != "completed"),
                )
                if artifact is not None:
                    try:
                        blob = api.get(artifact["archive_download_url"], binary=True)
                    except GitHubRequestError as exc:
                        # GitHub can expose artifact metadata before the archive
                        # redirect is readable. Retry only the observed transient
                        # 404, within the existing bounded deadline.
                        if exc.status != 404:
                            raise
                        previous = (
                            f"{previous} (artifact metadata ready; archive download pending)"
                        )
                    else:
                        try:
                            proof = validate_package_archive(
                                blob,
                                source_sha=source_sha,
                                engine_sha=engine_sha,
                                ardirec_sha=ardirec_sha,
                                workflow_run_id=run_id,
                                run_attempt=attempt,
                                event_name=event_name,
                                output_dir=output_dir,
                            )
                        except PackageProofError as exc:
                            if "identity or authority is invalid" not in str(exc):
                                raise
                            previous = f"{previous} (stale/different package identity)"
                        else:
                            proof.update({
                                "canonicalRunStatus": status,
                                "canonicalRunConclusion": conclusion,
                                "workflowCompleted": status == "completed",
                                "proofStage": (
                                    "completed-workflow"
                                    if status == "completed"
                                    else "sealed-package-artifact-ready"
                                ),
                                "releasePromotionAuthority": False,
                            })
                            return proof

        if time.monotonic() >= deadline:
            raise PackageProofError(
                "Timed out waiting for exact canonical Windows package artifact "
                f"for head={head_sha}, source={source_sha}, last={previous}"
            )
        time.sleep(min(poll_seconds, max(0.0, deadline - time.monotonic())))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--head-branch", required=True)
    parser.add_argument("--head-sha", required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--engine-sha", required=True)
    parser.add_argument("--ardirec-sha", required=True)
    parser.add_argument("--event-name", choices=("pull_request", "push"), default="pull_request")
    parser.add_argument("--wait-seconds", type=int, default=1200)
    parser.add_argument("--allow-in-progress-artifact", action="store_true")
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--proof-output", type=Path)
    args = parser.parse_args()

    proof = verify_canonical_package(
        GitHubReadOnly(os.environ.get("GITHUB_TOKEN", "")),
        repository=args.repository,
        branch=args.head_branch,
        head_sha=args.head_sha,
        source_sha=args.source_sha,
        engine_sha=args.engine_sha,
        ardirec_sha=args.ardirec_sha,
        event_name=args.event_name,
        wait_seconds=args.wait_seconds,
        allow_in_progress_artifact=args.allow_in_progress_artifact,
        output_dir=args.output_dir,
    )
    payload = json.dumps(proof, sort_keys=True)
    print("Canonical Windows package reuse PASS: " + payload)
    if args.proof_output:
        args.proof_output.parent.mkdir(parents=True, exist_ok=True)
        args.proof_output.write_text(payload + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
