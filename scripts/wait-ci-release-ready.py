#!/usr/bin/env python3
"""CI-P3G metadata-only release readiness gate.

Wait until the exact Build ARSAS canonical Windows package metadata exists and
the exact main-push Windows installer validation has completed successfully with
one non-expired installer artifact. This script never downloads artifact bytes;
the Windows release job remains responsible for full package/installer proof.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import time
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

SHA_RE = re.compile(r"^[0-9a-f]{40}$")
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+(?:[-.][0-9A-Za-z.-]+)?$")
PACKAGE_ARTIFACT = "ARSAS-windows-package-input"


class ReleaseReadyError(RuntimeError):
    pass


class GitHubReadOnly:
    def __init__(self, token: str):
        if not token:
            raise ReleaseReadyError("A read-only GITHUB_TOKEN is required")
        self.token = token

    def get(self, url: str) -> dict:
        req = urllib.request.Request(
            url,
            headers={
                "Authorization": f"Bearer {self.token}",
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": "2022-11-28",
                "User-Agent": "ARSAS-CI-P3G-Release-Ready",
            },
            method="GET",
        )
        try:
            with urllib.request.urlopen(req, timeout=30) as reply:
                return json.loads(reply.read(4 * 1024 * 1024))
        except (urllib.error.HTTPError, urllib.error.URLError, ValueError) as exc:
            raise ReleaseReadyError(f"GitHub release-readiness request failed: {exc}") from exc


def _sha(value: str, label: str) -> str:
    value = (value or "").strip().lower()
    if not SHA_RE.fullmatch(value):
        raise ReleaseReadyError(f"{label} must be a lowercase 40-hex SHA")
    return value


def _version(value: str) -> str:
    value = (value or "").strip()
    if value.lower().startswith("v"):
        value = value[1:]
    if not VERSION_RE.fullmatch(value):
        raise ReleaseReadyError("Release version is invalid")
    return value


def _xml_version(path: Path) -> str:
    try:
        root = ET.fromstring(path.read_text(encoding="utf-8-sig"))
    except (OSError, ET.ParseError) as exc:
        raise ReleaseReadyError(f"Cannot parse release version metadata: {path}") from exc
    for group in root.findall("PropertyGroup"):
        node = group.find("Version")
        if node is not None and node.text and node.text.strip():
            return node.text.strip()
    raise ReleaseReadyError(f"Version metadata is missing from {path}")


def resolve_identity(source_dir: Path, *, source_sha: str, ref_type: str, ref_name: str) -> dict:
    source_sha = _sha(source_sha, "source")
    if ref_type not in {"branch", "tag"}:
        raise ReleaseReadyError("Release gate supports only branch or tag push refs")

    version_file = (source_dir / "VERSION").read_text(encoding="utf-8-sig").strip()
    props_version = _xml_version(source_dir / "Directory.Build.props")
    project_version = _xml_version(source_dir / "ArIED61850Tester.csproj")

    if ref_type == "tag":
        requested = _version(ref_name)
    else:
        if ref_name != "main":
            raise ReleaseReadyError("Branch release readiness is restricted to main")
        try:
            release = json.loads((source_dir / ".release/windows.json").read_text(encoding="utf-8-sig"))
            requested = _version(str(release["version"]))
        except (OSError, ValueError, KeyError, TypeError) as exc:
            raise ReleaseReadyError("Release manifest version is missing or invalid") from exc

    canonical = _version(version_file)
    if requested != canonical or props_version != canonical or project_version != canonical:
        raise ReleaseReadyError(
            f"Release version identity mismatch: requested={requested}, VERSION={canonical}, "
            f"props={props_version}, project={project_version}"
        )

    try:
        iec = json.loads((source_dir / "engines/ARIEC61850.lock.json").read_text(encoding="utf-8-sig"))
        ard = json.loads((source_dir / "engines/ARDIREC.lock.json").read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        raise ReleaseReadyError("Release engine lock metadata cannot be decoded") from exc

    engine_sha = _sha(str(iec.get("commit", "")), "ARIEC61850")
    ardirec_sha = _sha(str(ard.get("commit", "")), "ArdIrec")
    if iec.get("repository") != "masarray/ARIEC61850" or iec.get("ref") != "main":
        raise ReleaseReadyError("ARIEC61850 release lock identity is invalid")
    if str(ard.get("repository", "")).lower() != "masarray/ardirec":
        raise ReleaseReadyError("ArdIrec release lock identity is invalid")
    bridge = ard.get("bridge") if isinstance(ard.get("bridge"), dict) else {}
    if ard.get("schema") != 3 or bridge.get("abi") != 1 or bridge.get("mode") != "native-only":
        raise ReleaseReadyError("ArdIrec native bridge release lock is invalid")

    return {
        "sourceSha": source_sha,
        "version": canonical,
        "engineSha": engine_sha,
        "ardirecSha": ardirec_sha,
        "refType": ref_type,
        "refName": ref_name,
    }


def _candidate_runs(payload: dict, *, workflow_name: str, source_sha: str) -> list[dict]:
    runs = payload.get("workflow_runs", [])
    if not isinstance(runs, list):
        raise ReleaseReadyError("GitHub workflow run listing is malformed")
    matches = [
        run for run in runs
        if run.get("name") == workflow_name
        and run.get("event") == "push"
        and run.get("head_branch") == "main"
        and run.get("head_sha") == source_sha
        and isinstance(run.get("id"), int)
        and isinstance(run.get("run_attempt"), int)
    ]
    return sorted(matches, key=lambda r: (r["id"], r["run_attempt"]), reverse=True)


def _one_artifact(payload: dict, *, name: str, run_id: int, allow_missing: bool) -> dict | None:
    artifacts = payload.get("artifacts", [])
    if not isinstance(artifacts, list):
        raise ReleaseReadyError("GitHub artifact listing is malformed")
    matches = [
        a for a in artifacts
        if a.get("name") == name
        and a.get("expired") is False
        and isinstance(a.get("id"), int)
        and a.get("workflow_run", {}).get("id", run_id) == run_id
    ]
    if not matches and allow_missing:
        return None
    if len(matches) != 1:
        raise ReleaseReadyError(f"Expected exactly one non-expired artifact named {name}")
    return matches[0]


def wait_release_ready(
    api: GitHubReadOnly,
    *,
    repository: str,
    identity: dict,
    wait_seconds: int = 2400,
    poll_seconds: int = 10,
) -> dict:
    if repository != "masarray/arsas":
        raise ReleaseReadyError("Release readiness is restricted to masarray/arsas")
    source_sha = _sha(identity.get("sourceSha", ""), "source")
    version = _version(identity.get("version", ""))
    if wait_seconds < 0 or poll_seconds < 1:
        raise ReleaseReadyError("Invalid bounded wait policy")

    base = f"https://api.github.com/repos/{repository}"
    query = urllib.parse.urlencode({"event": "push", "head_sha": source_sha, "per_page": 100})
    build_url = f"{base}/actions/workflows/build.yml/runs?{query}"
    installer_url = f"{base}/actions/workflows/installer-windows.yml/runs?{query}"
    deadline = time.monotonic() + wait_seconds
    last_build = "not found"
    last_installer = "not found"

    while True:
        package = None
        installer_artifact = None

        builds = _candidate_runs(
            api.get(build_url), workflow_name="Build ARSAS", source_sha=source_sha
        )
        build_run = builds[0] if builds else None
        if build_run is not None:
            last_build = (
                f"{build_run['id']}/{build_run['run_attempt']} "
                f"{build_run.get('status')}/{build_run.get('conclusion')}"
            )
            if build_run.get("status") == "completed" and build_run.get("conclusion") != "success":
                raise ReleaseReadyError(f"Latest exact Build ARSAS run failed: {last_build}")
            if build_run.get("status") in {"in_progress", "completed"}:
                package = _one_artifact(
                    api.get(f"{base}/actions/runs/{build_run['id']}/artifacts?per_page=100"),
                    name=PACKAGE_ARTIFACT,
                    run_id=build_run["id"],
                    allow_missing=(build_run.get("status") != "completed"),
                )

        installers = _candidate_runs(
            api.get(installer_url),
            workflow_name="Validate ARSAS Windows installer",
            source_sha=source_sha,
        )
        installer_run = installers[0] if installers else None
        if installer_run is not None:
            last_installer = (
                f"{installer_run['id']}/{installer_run['run_attempt']} "
                f"{installer_run.get('status')}/{installer_run.get('conclusion')}"
            )
            if installer_run.get("status") == "completed":
                if installer_run.get("conclusion") != "success":
                    raise ReleaseReadyError(
                        f"Latest exact installer validation failed: {last_installer}"
                    )
                installer_artifact = _one_artifact(
                    api.get(
                        f"{base}/actions/runs/{installer_run['id']}/artifacts?per_page=100"
                    ),
                    name=f"ARSAS-{version}-win-x64-installer",
                    run_id=installer_run["id"],
                    allow_missing=False,
                )

        if package is not None and installer_artifact is not None:
            return {
                **identity,
                "buildRunId": build_run["id"],
                "buildRunAttempt": build_run["run_attempt"],
                "packageArtifactId": package["id"],
                "installerRunId": installer_run["id"],
                "installerRunAttempt": installer_run["run_attempt"],
                "installerArtifactId": installer_artifact["id"],
                "metadataReady": True,
                "releasePromotionAuthority": False,
            }

        if time.monotonic() >= deadline:
            raise ReleaseReadyError(
                "Timed out waiting for exact release dependencies: "
                f"Build={last_build}, Installer={last_installer}"
            )
        time.sleep(min(poll_seconds, max(0.0, deadline - time.monotonic())))


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--repository", required=True)
    p.add_argument("--source-dir", type=Path, required=True)
    p.add_argument("--source-sha", required=True)
    p.add_argument("--ref-type", choices=("branch", "tag"), required=True)
    p.add_argument("--ref-name", required=True)
    p.add_argument("--wait-seconds", type=int, default=2400)
    p.add_argument("--poll-seconds", type=int, default=10)
    p.add_argument("--output", type=Path)
    args = p.parse_args()

    identity = resolve_identity(
        args.source_dir,
        source_sha=args.source_sha,
        ref_type=args.ref_type,
        ref_name=args.ref_name,
    )
    proof = wait_release_ready(
        GitHubReadOnly(os.environ.get("GITHUB_TOKEN", "")),
        repository=args.repository,
        identity=identity,
        wait_seconds=args.wait_seconds,
        poll_seconds=args.poll_seconds,
    )
    payload = json.dumps(proof, sort_keys=True)
    print("Release dependency metadata ready: " + payload)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(payload + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
