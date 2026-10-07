#!/usr/bin/env python3
"""Seal the exact tested Windows package payload produced by Build ARSAS."""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import re
import shutil
from pathlib import Path, PurePosixPath

_here = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location(
    "arsas_ci_canonical_proof", _here / "write-ci-canonical-proof.py"
)
assert _spec and _spec.loader
_canonical = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_canonical)
counters_from_trx = _canonical.counters_from_trx
sha = _canonical.sha

MANIFEST_NAME = "ci-windows-package-authority.json"
CANONICAL_MANIFEST = "ci-canonical-authority.json"
CANONICAL_TRX = "arsas-tests.trx"
KIND = "arsas-canonical-windows-package"
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+(?:[-.][0-9A-Za-z.-]+)?$")


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def safe_relative(path: Path, root: Path) -> str:
    rel = path.relative_to(root).as_posix()
    parsed = PurePosixPath(rel)
    if parsed.is_absolute() or ".." in parsed.parts or ":" in rel:
        raise ValueError(f"Unsafe staged path: {rel}")
    return rel


def copy_tree(source: Path, destination: Path) -> None:
    if not source.is_dir():
        raise FileNotFoundError(f"Directory not found: {source}")
    for item in source.rglob("*"):
        relative = item.relative_to(source)
        # actions/upload-artifact excludes hidden files by default. Hidden build/
        # coverage transients are not runtime package inputs, so exclude them
        # deliberately rather than widening artifact upload to hidden content.
        if any(part.startswith(".") for part in relative.parts):
            continue
        if item.is_symlink():
            raise ValueError(f"Symlinks are not permitted in package input: {item}")
        if not item.is_file():
            continue
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(item, target)


def verify_canonical_evidence(
    evidence_dir: Path, source_sha: str, engine_sha: str,
    run_id: int, run_attempt: int, event_name: str
) -> tuple[dict, bytes]:
    manifest_path = evidence_dir / CANONICAL_MANIFEST
    trx_path = evidence_dir / CANONICAL_TRX
    if not manifest_path.is_file() or not trx_path.is_file():
        raise FileNotFoundError("Canonical regression manifest/TRX is missing")

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    trx = trx_path.read_bytes()
    counters = counters_from_trx(trx)
    if (
        manifest.get("schemaVersion") != 1
        or manifest.get("kind") != "arsas-canonical-regression-evidence"
        or manifest.get("canonicalWorkflow") != "Build ARSAS"
        or manifest.get("workflowRunId") != run_id
        or manifest.get("runAttempt") != run_attempt
        or manifest.get("eventName") != event_name
        or manifest.get("exactSourceSha") != source_sha
        or manifest.get("exactEngineSha") != engine_sha
        or manifest.get("fullRegressionPassed") is not True
        or manifest.get("releasePromotionAuthority") is not False
        or manifest.get("testFile") != CANONICAL_TRX
        or manifest.get("testTrxSha256") != hashlib.sha256(trx).hexdigest()
        or manifest.get("testCounters") != counters
    ):
        raise ValueError("Canonical regression evidence does not match package identity")
    return manifest, trx


def file_record(path: Path, root: Path) -> dict:
    return {
        "path": safe_relative(path, root),
        "size": path.stat().st_size,
        "sha256": digest(path),
    }


def stage_and_create_manifest(
    *,
    artifact_root: Path,
    portable: Path,
    portable_identity: Path,
    installer_input: Path,
    verification_dir: Path,
    evidence_dir: Path,
    source_sha: str,
    engine_sha: str,
    ardirec_sha: str,
    version: str,
    runtime: str,
    workflow_run_id: int,
    run_attempt: int,
    event_name: str,
    test_assembly_relative: str,
    comtrade_fixture_relative: str,
    locus_fixture_relative: str,
) -> dict:
    source_sha = sha(source_sha, "source")
    engine_sha = sha(engine_sha, "engine")
    ardirec_sha = sha(ardirec_sha, "ArdIrec")
    if not VERSION_RE.fullmatch(version):
        raise ValueError("Invalid package version")
    if runtime != "win-x64":
        raise ValueError("Canonical Windows package runtime must be win-x64")
    if workflow_run_id <= 0 or run_attempt <= 0:
        raise ValueError("Invalid workflow run identity")
    if event_name not in {"push", "pull_request", "workflow_dispatch"}:
        raise ValueError("Unexpected workflow event")
    if not portable.is_file() or not portable_identity.is_file() or not installer_input.is_dir():
        raise FileNotFoundError("Portable, build identity, or installer-input payload is missing")

    canonical, trx = verify_canonical_evidence(
        evidence_dir, source_sha, engine_sha, workflow_run_id, run_attempt, event_name
    )

    if artifact_root.exists():
        shutil.rmtree(artifact_root)
    artifact_root.mkdir(parents=True)

    portable_target = artifact_root / "portable" / portable.name
    portable_target.parent.mkdir(parents=True)
    shutil.copy2(portable, portable_target)
    portable_identity_target = artifact_root / "portable" / portable_identity.name
    shutil.copy2(portable_identity, portable_identity_target)

    installer_target = artifact_root / "installer-input" / installer_input.name
    copy_tree(installer_input, installer_target)

    evidence_target = artifact_root / "evidence"
    evidence_target.mkdir()
    shutil.copy2(evidence_dir / CANONICAL_MANIFEST, evidence_target / CANONICAL_MANIFEST)
    (evidence_target / CANONICAL_TRX).write_bytes(trx)

    verification_target = artifact_root / "verification"
    copy_tree(verification_dir, verification_target)

    bridge = installer_target / "Tools" / "ArdIrec" / "ardirec_bridge.dll"
    test_assembly = verification_target / PurePosixPath(test_assembly_relative)
    comtrade_fixture = verification_target / PurePosixPath(comtrade_fixture_relative)
    locus_fixture = verification_target / PurePosixPath(locus_fixture_relative)
    for label, path in (
        ("native bridge", bridge),
        ("test assembly", test_assembly),
        ("COMTRADE fixture", comtrade_fixture),
        ("locus fixture", locus_fixture),
    ):
        if not path.is_file():
            raise FileNotFoundError(f"Required {label} missing from sealed payload: {path}")

    files = []
    for path in sorted(artifact_root.rglob("*"), key=lambda p: p.as_posix().lower()):
        if path.is_symlink():
            raise ValueError(f"Symlink not allowed in sealed payload: {path}")
        if path.is_file() and path.name != MANIFEST_NAME:
            files.append(file_record(path, artifact_root))

    by_path = {entry["path"]: entry for entry in files}
    portable_rel = safe_relative(portable_target, artifact_root)
    portable_identity_rel = safe_relative(portable_identity_target, artifact_root)
    bridge_rel = safe_relative(bridge, artifact_root)
    test_rel = safe_relative(test_assembly, artifact_root)
    comtrade_rel = safe_relative(comtrade_fixture, artifact_root)
    locus_rel = safe_relative(locus_fixture, artifact_root)

    try:
        build_identity = json.loads(portable_identity_target.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        raise ValueError("Portable build identity cannot be decoded") from exc

    if (
        build_identity.get("schemaVersion") != 1
        or build_identity.get("kind") != "arsas-portable-build-identity"
        or build_identity.get("sourceCommit") != source_sha
        or build_identity.get("engineCommit") != engine_sha
        or build_identity.get("ardIrecLockCommit") != ardirec_sha
        or build_identity.get("portableSha256") != by_path[portable_rel]["sha256"]
        or build_identity.get("portableSizeBytes") != by_path[portable_rel]["size"]
        or build_identity.get("ardIrecBridgeSha256") != by_path[bridge_rel]["sha256"]
        or build_identity.get("ardIrecBridgeSizeBytes") != by_path[bridge_rel]["size"]
        or build_identity.get("deterministicManagedBuild") is not True
        or build_identity.get("reproducibleNativeLinkRequested") is not True
    ):
        raise ValueError("Portable build identity does not match sealed package authority")

    manifest = {
        "schemaVersion": 1,
        "kind": KIND,
        "canonicalWorkflow": "Build ARSAS",
        "workflowRunId": workflow_run_id,
        "runAttempt": run_attempt,
        "eventName": event_name,
        "sourceSha": source_sha,
        "engineSha": engine_sha,
        "ardirecSha": ardirec_sha,
        "version": version,
        "runtime": runtime,
        "fullRegressionPassed": True,
        "testCounters": canonical["testCounters"],
        "canonicalTestManifestSha256": digest(evidence_target / CANONICAL_MANIFEST),
        "testTrxSha256": hashlib.sha256(trx).hexdigest(),
        "nativeBridgeIntegrationPassed": True,
        "portableSmokePassed": True,
        "installerBuilt": False,
        "releasePromotionAuthority": False,
        "installerInputRoot": safe_relative(installer_target, artifact_root),
        "portable": by_path[portable_rel],
        "portableIdentity": by_path[portable_identity_rel],
        "nativeBridge": by_path[bridge_rel],
        "verification": {
            "testAssemblyPath": test_rel,
            "comtradeFixturePath": comtrade_rel,
            "locusFixturePath": locus_rel,
        },
        "fileCount": len(files),
        "totalBytes": sum(entry["size"] for entry in files),
        "files": files,
    }
    (artifact_root / MANIFEST_NAME).write_text(
        json.dumps(manifest, sort_keys=True, indent=2) + "\n", encoding="utf-8"
    )
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-root", type=Path, required=True)
    parser.add_argument("--portable", type=Path, required=True)
    parser.add_argument("--portable-identity", type=Path, required=True)
    parser.add_argument("--installer-input", type=Path, required=True)
    parser.add_argument("--verification-dir", type=Path, required=True)
    parser.add_argument("--evidence-dir", type=Path, required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--engine-sha", required=True)
    parser.add_argument("--ardirec-sha", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--runtime", default="win-x64")
    parser.add_argument("--run-id", type=int, required=True)
    parser.add_argument("--attempt", type=int, required=True)
    parser.add_argument("--event-name", required=True)
    parser.add_argument(
        "--test-assembly-relative",
        default="ARSAS.Tests/ARSAS.Tests.dll",
    )
    parser.add_argument(
        "--comtrade-fixture-relative",
        default="fixtures/minimal_1999.cfg",
    )
    parser.add_argument(
        "--locus-fixture-relative",
        default="fixtures/distance_p1.cfg",
    )
    args = parser.parse_args()

    manifest = stage_and_create_manifest(
        artifact_root=args.artifact_root,
        portable=args.portable,
        portable_identity=args.portable_identity,
        installer_input=args.installer_input,
        verification_dir=args.verification_dir,
        evidence_dir=args.evidence_dir,
        source_sha=args.source_sha,
        engine_sha=args.engine_sha,
        ardirec_sha=args.ardirec_sha,
        version=args.version,
        runtime=args.runtime,
        workflow_run_id=args.run_id,
        run_attempt=args.attempt,
        event_name=args.event_name,
        test_assembly_relative=args.test_assembly_relative,
        comtrade_fixture_relative=args.comtrade_fixture_relative,
        locus_fixture_relative=args.locus_fixture_relative,
    )
    print(
        "Canonical Windows package sealed: "
        f"source={manifest['sourceSha']} engine={manifest['engineSha']} "
        f"ardirec={manifest['ardirecSha']} files={manifest['fileCount']} "
        f"bytes={manifest['totalBytes']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
