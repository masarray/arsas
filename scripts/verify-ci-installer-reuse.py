#!/usr/bin/env python3
"""Verify and safely materialize the exact installed-smoke-tested ARSAS installer."""
from __future__ import annotations

import argparse, hashlib, io, json, os, re, time, urllib.error, urllib.parse, urllib.request, zipfile
from pathlib import Path, PurePosixPath

EXPECTED_MANIFEST = "ci-installer-authority.json"
MAX_ARCHIVE_BYTES = 256 * 1024 * 1024
MAX_FILE_BYTES = 192 * 1024 * 1024
SHA_RE = re.compile(r"^[0-9a-f]{40}$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


class InstallerProofError(RuntimeError):
    pass


class GitHubRequestError(InstallerProofError):
    def __init__(self, message: str, *, status: int | None = None):
        super().__init__(message)
        self.status = status


class StripCrossOriginAuthorization(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        redirected = super().redirect_request(request, fp, code, msg, headers, newurl)
        if redirected is not None:
            if urllib.parse.urlparse(request.full_url).netloc.lower() != urllib.parse.urlparse(newurl).netloc.lower():
                redirected.remove_header("Authorization")
        return redirected


class GitHubReadOnly:
    def __init__(self, token: str):
        if not token:
            raise InstallerProofError("A read-only GITHUB_TOKEN is required")
        self.token = token

    def get(self, url: str, *, binary: bool = False):
        req = urllib.request.Request(url, headers={
            "Authorization": f"Bearer {self.token}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "ARSAS-CI-P3D-Installer-Reuse",
        })
        try:
            with urllib.request.build_opener(StripCrossOriginAuthorization()).open(req, timeout=30) as reply:
                return reply.read(MAX_ARCHIVE_BYTES + 1) if binary else json.loads(reply.read(3 * 1024 * 1024))
        except urllib.error.HTTPError as exc:
            raise GitHubRequestError(f"GitHub installer evidence request failed: HTTP {exc.code}", status=exc.code) from exc
        except (urllib.error.URLError, ValueError) as exc:
            raise GitHubRequestError(f"GitHub installer evidence request failed: {exc}") from exc


def _sha(value: str, label: str) -> str:
    value = (value or "").strip().lower()
    if not SHA_RE.fullmatch(value):
        raise InstallerProofError(f"{label} must be a lowercase 40-hex SHA")
    return value


def _safe_name(name: str) -> PurePosixPath:
    norm = name.replace("\\", "/")
    p = PurePosixPath(norm)
    if p.is_absolute() or ".." in p.parts or ":" in norm:
        raise InstallerProofError("Installer artifact contains an unsafe path")
    return p


def validate_archive(blob: bytes, *, version: str, source_sha: str, engine_sha: str,
                     ardirec_sha: str, package_artifact_sha256: str,
                     workflow_run_id: int, run_attempt: int,
                     output_dir: Path | None = None) -> dict:
    if not blob or len(blob) > MAX_ARCHIVE_BYTES:
        raise InstallerProofError("Installer artifact is missing or oversized")
    try:
        z = zipfile.ZipFile(io.BytesIO(blob))
    except zipfile.BadZipFile as exc:
        raise InstallerProofError("Installer artifact is not a valid ZIP") from exc

    files: dict[str, tuple[zipfile.ZipInfo, bytes]] = {}
    seen = set()
    with z:
        for info in z.infolist():
            if info.is_dir():
                continue
            p = _safe_name(info.filename)
            key = "/".join(part.rstrip(" .").casefold() for part in p.parts)
            if key in seen:
                raise InstallerProofError("Installer artifact has a Windows-normalized path collision")
            seen.add(key)
            if info.file_size < 0 or info.file_size > MAX_FILE_BYTES:
                raise InstallerProofError("Installer artifact contains an oversized file")
            data = z.read(info)
            files[p.name] = (info, data)

    if EXPECTED_MANIFEST not in files:
        raise InstallerProofError("Installer authority manifest is missing")
    try:
        manifest = json.loads(files[EXPECTED_MANIFEST][1])
    except ValueError as exc:
        raise InstallerProofError("Installer authority manifest is invalid JSON") from exc

    expected = {
        "schemaVersion": 1,
        "kind": "arsas-validated-windows-installer",
        "version": version,
        "sourceSha": _sha(source_sha, "source"),
        "engineSha": _sha(engine_sha, "engine"),
        "ardirecSha": _sha(ardirec_sha, "ArdIrec"),
        "canonicalPackageArtifactSha256": package_artifact_sha256.lower(),
        "installerWorkflow": "Validate ARSAS Windows installer",
        "workflowRunId": workflow_run_id,
        "runAttempt": run_attempt,
        "eventName": "push",
        "installedSmokePassed": True,
        "releasePromotionAuthority": False,
    }
    for key, value in expected.items():
        if manifest.get(key) != value:
            raise InstallerProofError(f"Installer authority mismatch for {key}")

    installer_name = manifest.get("installerFile")
    if not isinstance(installer_name, str) or installer_name not in files:
        raise InstallerProofError("Validated installer file is missing")
    installer = files[installer_name][1]
    actual_sha = hashlib.sha256(installer).hexdigest()
    if manifest.get("installerSha256") != actual_sha:
        raise InstallerProofError("Installer SHA-256 differs from authority manifest")
    if manifest.get("installerSizeBytes") != len(installer):
        raise InstallerProofError("Installer size differs from authority manifest")
    if not SHA256_RE.fullmatch(package_artifact_sha256.lower()):
        raise InstallerProofError("Canonical package artifact SHA-256 is invalid")

    if output_dir is not None:
        output_dir.mkdir(parents=True, exist_ok=True)
        target = output_dir / installer_name
        target.write_bytes(installer)
        (output_dir / EXPECTED_MANIFEST).write_bytes(files[EXPECTED_MANIFEST][1])

    return {
        "installerFile": installer_name,
        "installerSha256": actual_sha,
        "installerSizeBytes": len(installer),
        "installerRunId": workflow_run_id,
        "installerRunAttempt": run_attempt,
        "canonicalPackageRun": manifest.get("canonicalPackageRun"),
        "canonicalPackageArtifactSha256": manifest.get("canonicalPackageArtifactSha256"),
        "artifactSha256": hashlib.sha256(blob).hexdigest(),
        "sourceSha": manifest["sourceSha"],
        "engineSha": manifest["engineSha"],
        "ardirecSha": manifest["ardirecSha"],
        "version": manifest["version"],
        "installedSmokePassed": True,
        "releasePromotionAuthority": False,
    }


def verify(api: GitHubReadOnly, *, repository: str, source_sha: str, version: str,
           engine_sha: str, ardirec_sha: str, package_artifact_sha256: str,
           wait_seconds: int, poll_seconds: int, output_dir: Path | None) -> dict:
    if repository != "masarray/arsas":
        raise InstallerProofError("Installer reuse is restricted to masarray/arsas")
    _sha(source_sha, "source"); _sha(engine_sha, "engine"); _sha(ardirec_sha, "ArdIrec")
    base = f"https://api.github.com/repos/{repository}"
    query = urllib.parse.urlencode({"event": "push", "head_sha": source_sha, "per_page": 100})
    runs_url = f"{base}/actions/workflows/installer-windows.yml/runs?{query}"
    deadline = time.monotonic() + wait_seconds
    last = None
    while True:
        payload = api.get(runs_url)
        runs = [r for r in payload.get("workflow_runs", [])
                if r.get("name") == "Validate ARSAS Windows installer"
                and r.get("event") == "push" and r.get("head_branch") == "main"
                and r.get("head_sha") == source_sha
                and isinstance(r.get("id"), int) and isinstance(r.get("run_attempt"), int)]
        runs.sort(key=lambda r: (r["id"], r["run_attempt"]), reverse=True)
        if runs:
            run = runs[0]
            last = f"{run['id']}/{run['run_attempt']} {run.get('status')}/{run.get('conclusion')}"
            if run.get("status") == "completed":
                if run.get("conclusion") != "success":
                    raise InstallerProofError(f"Latest exact installer validation failed: {last}")
                arts = api.get(f"{base}/actions/runs/{run['id']}/artifacts?per_page=100").get("artifacts", [])
                name = f"ARSAS-{version}-win-x64-installer"
                matches = [a for a in arts if a.get("name") == name and a.get("expired") is False]
                if len(matches) != 1:
                    raise InstallerProofError("Expected exactly one non-expired validated installer artifact")
                try:
                    blob = api.get(matches[0]["archive_download_url"], binary=True)
                except GitHubRequestError as exc:
                    # Artifact metadata can become visible shortly before GitHub's
                    # archive redirect is readable. Retry only this transient 404
                    # within the existing bounded deadline; all other errors fail.
                    if exc.status != 404:
                        raise
                    last = (
                        f"{last} (installer metadata ready; archive download pending)"
                    )
                else:
                    proof = validate_archive(
                        blob, version=version, source_sha=source_sha, engine_sha=engine_sha,
                        ardirec_sha=ardirec_sha, package_artifact_sha256=package_artifact_sha256,
                        workflow_run_id=run["id"], run_attempt=run["run_attempt"], output_dir=output_dir)
                    proof["installerRunStatus"] = "completed"
                    proof["installerRunConclusion"] = "success"
                    return proof
        if time.monotonic() >= deadline:
            raise InstallerProofError(f"Timed out waiting for exact validated installer, last={last}")
        time.sleep(min(poll_seconds, max(0.0, deadline - time.monotonic())))


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--repository", required=True)
    p.add_argument("--source-sha", required=True)
    p.add_argument("--version", required=True)
    p.add_argument("--engine-sha", required=True)
    p.add_argument("--ardirec-sha", required=True)
    p.add_argument("--package-artifact-sha256", required=True)
    p.add_argument("--wait-seconds", type=int, default=1800)
    p.add_argument("--poll-seconds", type=int, default=10)
    p.add_argument("--output-dir", type=Path, required=True)
    p.add_argument("--proof-output", type=Path, default=None)
    args = p.parse_args()
    proof = verify(
        GitHubReadOnly(os.environ.get("GITHUB_TOKEN", "")),
        repository=args.repository, source_sha=args.source_sha, version=args.version,
        engine_sha=args.engine_sha, ardirec_sha=args.ardirec_sha,
        package_artifact_sha256=args.package_artifact_sha256,
        wait_seconds=args.wait_seconds, poll_seconds=args.poll_seconds, output_dir=args.output_dir)
    payload = json.dumps(proof, sort_keys=True)
    print("Validated installer reuse PASS: " + payload)
    if args.proof_output:
        args.proof_output.write_text(payload + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
