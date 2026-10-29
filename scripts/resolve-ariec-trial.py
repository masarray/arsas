#!/usr/bin/env python3
"""Select an exact-SHA nonpromotion engine trial without altering ARSAS's production pin."""
import argparse
import json
import re
import subprocess
from pathlib import Path

CMD = "engines/ARIEC61850.trial.json"
REPORT = "engines/ARIEC61850.reporting-trial.json"
SHA = re.compile(r"^[0-9a-f]{40}$")


def resolve(root, changed):
    if CMD in changed and REPORT in changed:
        raise ValueError("competing engine trial manifests")
    focus = "reporting" if REPORT in changed else "command-read"
    manifest_path = REPORT if focus == "reporting" else CMD
    lock = json.loads((root / "engines/ARIEC61850.lock.json").read_text(encoding="utf-8"))
    trial = json.loads((root / manifest_path).read_text(encoding="utf-8"))
    baseline, candidate = lock.get("commit"), trial.get("commit")
    if not SHA.fullmatch(str(baseline)) or not SHA.fullmatch(str(candidate)):
        raise ValueError("invalid immutable engine SHA")
    if lock.get("repository") != "masarray/ARIEC61850" or lock.get("ref") != "main":
        raise ValueError("approved engine provenance mismatch")
    if trial.get("runMode") != "fast-dev-nonpromotion" or trial.get("scope") != focus:
        raise ValueError("trial runMode/scope mismatch")
    if candidate == baseline:
        raise ValueError("trial engine equals approved production pin")
    if focus == "reporting" and (
        trial.get("approvedBaselineCommit") != baseline
        or trial.get("repository") != lock["repository"]
        or any(trial.get(key) is not True for key in
               ("mainlinePromotionForbidden", "physicalQualificationRequired",
                "cyclicMmsProcessPollingForbidden", "automaticRcbMutationForbidden"))
    ):
        raise ValueError("reporting safety/provenance guard violation")
    return candidate, focus


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    diff = set(subprocess.check_output(
        ["git", "diff", "--name-only", "HEAD^1", "HEAD"], text=True).splitlines())
    sha, focus = resolve(Path.cwd(), diff)
    with args.output.open("a", encoding="utf-8") as f:
        f.write(f"sha={sha}\nfocus={focus}\n")
    print(f"CI-only {focus} trial engine: {sha}")
