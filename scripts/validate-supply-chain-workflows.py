#!/usr/bin/env python3
"""Validate ARSAS primary release, backfill and SPDX supply-chain contracts."""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS = ROOT / ".github" / "workflows"


def exercise_sbom(errors: list[str]) -> None:
    with tempfile.TemporaryDirectory(prefix="arsas-sbom-") as temporary:
        root = Path(temporary)
        package = root / "package"
        package.mkdir()
        contents = {
            "alpha.txt": b"alpha\n",
            "nested/beta.bin": bytes(range(16)),
        }
        sha1_values: list[str] = []
        for relative, data in contents.items():
            target = package / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            sha1_values.append(hashlib.sha1(data).hexdigest())
        output = root / "fixture.spdx.json"
        completed = subprocess.run(
            [
                sys.executable,
                str(ROOT / "scripts" / "generate-release-sbom.py"),
                "--package-dir", str(package),
                "--version", "1.6.18",
                "--source-commit", "63ffee62d073486a67ad37921a7c544661203735",
                "--created", "2026-07-22T00:00:00Z",
                "--output", str(output),
            ],
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )
        if completed.returncode != 0:
            errors.append("SPDX generator fixture failed: " + (completed.stderr.strip() or completed.stdout.strip()))
            return
        value = json.loads(output.read_text(encoding="utf-8"))
        expected_code = hashlib.sha1("".join(sorted(sha1_values)).encode("ascii")).hexdigest()
        package_value = value.get("packages", [{}])[0]
        observed_code = package_value.get("packageVerificationCode", {}).get("packageVerificationCodeValue")
        if observed_code != expected_code:
            errors.append(f"SPDX package verification code is {observed_code!r}, expected {expected_code!r}")
        files = value.get("files", [])
        if len(files) != len(contents):
            errors.append("SPDX generator fixture has an unexpected file count")
        for item in files:
            algorithms = {entry.get("algorithm") for entry in item.get("checksums", [])}
            if algorithms != {"SHA1", "SHA256"}:
                errors.append(f"SPDX file checksum algorithms are invalid: {algorithms}")
        if value.get("creationInfo", {}).get("created") != "2026-07-22T00:00:00Z":
            errors.append("SPDX generator did not preserve the supplied UTC source timestamp")


def main() -> int:
    errors: list[str] = []
    primary = (WORKFLOWS / "release-windows.yml").read_text(encoding="utf-8")
    backfill = (WORKFLOWS / "release-supply-chain.yml").read_text(encoding="utf-8")

    primary_contract = (
        "id-token: write", "attestations: write", "Setup Python 3.12",
        "generate-release-sbom.py", "ARSAS-Windows-x64-SBOM.spdx.json",
        "actions/attest@v4", "subject-path: ArIED61850Tester/dist/ARSAS-Windows-x64-Setup.exe",
        "sbom-path: ArIED61850Tester/dist/ARSAS-Windows-x64-SBOM.spdx.json",
        "supplyChain = [ordered]@{", "attestationWorkflow = \"release-windows.yml\"",
    )
    for value in primary_contract:
        if value not in primary:
            errors.append(f"release-windows.yml missing primary supply-chain contract: {value}")

    backfill_contract = (
        "name: Backfill ARSAS release supply chain", "workflow_dispatch:",
        "sha256sum --check", "git rev-parse HEAD", "generate-release-sbom.py",
        "actions/attest@v4", "post-publication evidence does not retroactively describe another tag",
    )
    for value in backfill_contract:
        if value not in backfill:
            errors.append(f"release-supply-chain.yml missing backfill contract: {value}")
    if "\n  release:" in backfill:
        errors.append("release-supply-chain.yml must not auto-run after the primary release workflow")
    if "github.event.release" in backfill:
        errors.append("release-supply-chain.yml must use an explicit tag input only")

    exercise_sbom(errors)

    if errors:
        print("ARSAS supply-chain workflow validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print("ARSAS supply-chain validation passed: primary build attestations, explicit backfill and SPDX verification code are correct.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
