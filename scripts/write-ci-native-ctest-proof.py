#!/usr/bin/env python3
"""Seal and reverify canonical ArdIrec CTest/JUnit evidence (CI-P3J-A).

Evidence capture is NOT permission to skip the independent COMTRADE lane.
The existing Windows package remains the authority for exact tested bytes.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

KIND = "arsas-canonical-ardirec-native-ctest"
SHA_RE = re.compile(r"^[0-9a-f]{40}$")
MAX_JUNIT_BYTES = 2 * 1024 * 1024
MAX_TESTS = 5000
PROFILE = "windows-x64-release-native-only-cmake-ctest"


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def parse_ctest_junit(path: Path) -> list[str]:
    payload = path.read_bytes()
    if not payload or len(payload) > MAX_JUNIT_BYTES:
        raise ValueError("CTest JUnit evidence missing or exceeds bounded size")
    if b"<!DOCTYPE" in payload.upper() or b"<!ENTITY" in payload.upper():
        raise ValueError("DTD and entities are prohibited in CTest evidence")
    try:
        root = ET.fromstring(payload)
    except ET.ParseError as exc:
        raise ValueError("CTest JUnit evidence is not valid XML") from exc
    if local_name(root.tag) not in {"testsuite", "testsuites"}:
        raise ValueError("CTest JUnit root must be testsuite/testsuites")

    suites = [node for node in root.iter() if local_name(node.tag) == "testsuite"]
    if not suites:
        raise ValueError("CTest JUnit contains no test suites")

    names: list[str] = []
    for suite in suites:
        cases = [node for node in suite if local_name(node.tag) == "testcase"]
        if "tests" in suite.attrib and int(suite.attrib["tests"]) != len(cases):
            raise ValueError("CTest JUnit suite test counter disagrees with cases")
        for counter in ("failures", "errors", "skipped", "disabled"):
            if int(suite.attrib.get(counter, "0")) != 0:
                raise ValueError(f"CTest JUnit reports nonzero {counter}")
        for case in cases:
            name = case.attrib.get("name", "").strip()
            if not name:
                raise ValueError("CTest JUnit test case name is missing")
            if case.attrib.get("status", "").lower() in {"notrun", "skipped", "disabled"}:
                raise ValueError("CTest JUnit contains an unexecuted native test")
            if any(local_name(child.tag) in {"failure", "error", "skipped"} for child in case):
                raise ValueError("CTest JUnit includes failing/skipped native test")
            names.append(name)

    if not names or len(names) > MAX_TESTS or len(names) != len(set(names)):
        raise ValueError("CTest JUnit has empty, duplicate, or excessive test cases")
    return sorted(names)


def expected_proof(
    *, junit: Path, bridge: Path, source_sha: str, engine_sha: str,
    ardirec_sha: str, run_id: int, attempt: int, event: str,
) -> dict:
    for label, value in (
        ("source", source_sha), ("engine", engine_sha), ("ArdIrec", ardirec_sha)
    ):
        if not SHA_RE.fullmatch(value):
            raise ValueError(f"Invalid {label} SHA")
    if run_id < 1 or attempt < 1 or event not in {
        "push", "pull_request", "workflow_dispatch"
    }:
        raise ValueError("Invalid canonical GitHub Actions run identity")
    if not bridge.is_file() or bridge.stat().st_size < 1:
        raise ValueError("Native bridge is missing or empty")
    names = parse_ctest_junit(junit)
    return {
        "schemaVersion": 1,
        "kind": KIND,
        "canonicalWorkflow": "Build ARSAS",
        "workflowRunId": run_id,
        "runAttempt": attempt,
        "eventName": event,
        "sourceSha": source_sha,
        "engineSha": engine_sha,
        "ardirecSha": ardirec_sha,
        "buildProfile": PROFILE,
        "ctestJunitSha256": digest(junit),
        "nativeBridgeSha256": digest(bridge),
        "nativeBridgeSizeBytes": bridge.stat().st_size,
        "testNames": names,
        "nativeTestsPassed": len(names),
        "nativeTestsFailed": 0,
        "nativeTestsSkipped": 0,
        "nativeCTestPassed": True,
        "independentComtradeLaneReplaced": False,
        "releasePromotionAuthority": False,
    }


def write_or_verify(proof_path: Path, *, verify_only: bool, **kwargs) -> dict:
    expected = expected_proof(**kwargs)
    if verify_only:
        try:
            actual = json.loads(proof_path.read_text(encoding="utf-8-sig"))
        except (OSError, UnicodeError, ValueError) as exc:
            raise ValueError("Native CTest proof is missing or invalid") from exc
        if actual != expected:
            raise ValueError("Native CTest proof does not match exact tested bytes/identity")
    else:
        proof_path.parent.mkdir(parents=True, exist_ok=True)
        proof_path.write_text(
            json.dumps(expected, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
    return expected


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--junit", type=Path, required=True)
    parser.add_argument("--bridge", type=Path, required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--engine-sha", required=True)
    parser.add_argument("--ardirec-sha", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    parser.add_argument("--attempt", type=int, required=True)
    parser.add_argument("--event", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()
    proof = write_or_verify(
        args.output, verify_only=args.verify_only, junit=args.junit,
        bridge=args.bridge, source_sha=args.source_sha,
        engine_sha=args.engine_sha, ardirec_sha=args.ardirec_sha,
        run_id=args.run_id, attempt=args.attempt, event=args.event,
    )
    print(
        f"Native CTest proof PASS: {proof['nativeTestsPassed']} tests; "
        f"bridge={proof['nativeBridgeSha256']}; "
        "independent COMTRADE authority unchanged"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
