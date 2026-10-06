#!/usr/bin/env python3
"""Deterministic, read-only inventory and integrity guard for GitHub Actions workflows.

CI-P0 deliberately avoids a YAML dependency. It inspects the stable structural subset
used by this repository: tracked workflow files, top-level workflow keys, job IDs,
step names and expensive CI primitives. GitHub remains the YAML parser of record.

The guard does not decide whether an existing workflow is semantically necessary.
It prevents accidental workflow duplication/corruption and makes CI-cost growth
explicit through a reviewed budget file.
"""
from __future__ import annotations

import argparse
import collections
import json
import re
import subprocess
from pathlib import Path
from typing import Iterable

WORKFLOW_PREFIX = ".github/workflows/"
WORKFLOW_SUFFIXES = (".yml", ".yaml")

PRIMITIVES = {
    "actionsCheckout": "actions/checkout@",
    "actionsSetupDotnet": "actions/setup-dotnet@",
    "actionsSetupPython": "actions/setup-python@",
    "actionsUploadArtifact": "actions/upload-artifact@",
    "actionsDownloadArtifact": "actions/download-artifact@",
    "dotnetRestore": "dotnet restore",
    "dotnetBuild": "dotnet build",
    "dotnetTest": "dotnet test",
    "dotnetPublish": "dotnet publish",
    "workflowCall": "workflow_call:",
    "concurrencyBlocks": "concurrency:",
}


def git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(
        ("git", "-C", str(root), *args),
        stderr=subprocess.PIPE,
    )


def tracked_paths(root: Path) -> list[str]:
    return sorted(
        {
            item.decode("utf-8", "surrogateescape").replace("\\", "/")
            for item in git(root, "ls-files", "-z").split(b"\0")
            if item
        }
    )


def workflow_paths(root: Path) -> list[str]:
    return [
        path
        for path in tracked_paths(root)
        if path.startswith(WORKFLOW_PREFIX)
        and Path(path).suffix.lower() in WORKFLOW_SUFFIXES
    ]


def events(content: str) -> list[str]:
    found: set[str] = set()
    in_on = False
    for line in content.splitlines():
        if re.match(r"^on:\s*(?:#.*)?$", line):
            in_on = True
            continue
        inline = re.match(r"^on:\s*\[([^]]+)\]", line)
        if inline:
            found.update(
                item.strip().strip("'\"")
                for item in inline.group(1).split(",")
                if item.strip()
            )
            break
        if in_on and line and not line[0].isspace() and not line.lstrip().startswith("#"):
            break
        if in_on:
            match = re.match(r"^  ([\w-]+):(?:\s|$)", line)
            if match:
                found.add(match.group(1))
    return sorted(found)


def primitive_counts(content: str) -> dict[str, int]:
    return {name: content.count(token) for name, token in PRIMITIVES.items()}


def structure_findings(path: str, content: str) -> tuple[dict, list[str]]:
    lines = content.splitlines()
    errors: list[str] = []

    top_name_count = sum(1 for line in lines if re.match(r"^name:\s*\S", line))
    jobs_count = sum(1 for line in lines if re.match(r"^jobs:\s*(?:#.*)?$", line))
    if top_name_count != 1:
        errors.append(
            f"{path}: expected exactly one top-level workflow name, found {top_name_count}."
        )
    if jobs_count != 1:
        errors.append(f"{path}: expected exactly one top-level jobs block, found {jobs_count}.")

    in_jobs = False
    current_job: str | None = None
    job_ids: list[str] = []
    step_names: dict[str, list[str]] = collections.defaultdict(list)

    for line in lines:
        if re.match(r"^jobs:\s*(?:#.*)?$", line):
            in_jobs = True
            current_job = None
            continue

        if in_jobs and line and not line[0].isspace() and not line.lstrip().startswith("#"):
            in_jobs = False
            current_job = None

        if not in_jobs:
            continue

        job_match = re.match(r"^  ([A-Za-z0-9_-]+):\s*(?:#.*)?$", line)
        if job_match:
            current_job = job_match.group(1)
            job_ids.append(current_job)
            continue

        if current_job is None:
            continue

        step_match = re.match(r"^\s{6}-\s+name:\s*(.+?)\s*$", line)
        if step_match:
            raw_name = step_match.group(1).strip().strip("'\"")
            step_names[current_job].append(raw_name)

    duplicate_jobs = sorted(
        job for job, count in collections.Counter(job_ids).items() if count > 1
    )
    for job in duplicate_jobs:
        errors.append(f"{path}: duplicate job id '{job}'.")

    duplicate_steps: dict[str, list[str]] = {}
    for job, names in step_names.items():
        duplicates = sorted(
            name for name, count in collections.Counter(names).items() if count > 1
        )
        if duplicates:
            duplicate_steps[job] = duplicates
            for name in duplicates:
                errors.append(
                    f"{path}: job '{job}' contains duplicate step name '{name}'."
                )

    return {
        "topLevelNameCount": top_name_count,
        "topLevelJobsCount": jobs_count,
        "jobIds": job_ids,
        "duplicateJobIds": duplicate_jobs,
        "duplicateStepNamesByJob": duplicate_steps,
    }, errors


def inventory(root: Path) -> dict:
    workflows: list[dict] = []
    structure_errors: list[str] = []
    totals = collections.Counter()

    for path in workflow_paths(root):
        full = root / path
        if not full.is_file():
            raise FileNotFoundError(f"Tracked workflow is missing: {path}")
        content = full.read_text(encoding="utf-8-sig", errors="replace")

        metrics = primitive_counts(content)
        structure, errors = structure_findings(path, content)
        structure_errors.extend(errors)
        totals.update(metrics)

        workflows.append(
            {
                "path": path,
                "events": events(content),
                "metrics": metrics,
                "structure": structure,
            }
        )

    totals["workflowFiles"] = len(workflows)
    totals["workflowsWithDotnetBuild"] = sum(
        1 for workflow in workflows if workflow["metrics"]["dotnetBuild"] > 0
    )
    totals["workflowsWithDotnetTest"] = sum(
        1 for workflow in workflows if workflow["metrics"]["dotnetTest"] > 0
    )
    totals["pullRequestWorkflows"] = sum(
        1 for workflow in workflows if "pull_request" in workflow["events"]
    )

    return {
        "schemaVersion": 1,
        "sourceCommit": git(root, "rev-parse", "HEAD").decode("ascii").strip(),
        "scope": (
            "Tracked GitHub Actions workflows only. Counts are textual CI-cost indicators; "
            "they do not prove runtime duration or semantic necessity."
        ),
        "totals": dict(sorted(totals.items())),
        "structureErrors": sorted(structure_errors),
        "workflows": workflows,
    }


def load_budget(path: Path) -> dict:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schemaVersion") != 1:
        raise ValueError("Unsupported CI workflow budget schema.")
    if not isinstance(data.get("limits"), dict):
        raise ValueError("CI workflow budget is missing 'limits'.")
    return data


def budget_violations(report: dict, budget: dict) -> list[str]:
    violations: list[str] = []
    totals = report["totals"]
    for metric, limit in sorted(budget["limits"].items()):
        if metric not in totals:
            violations.append(f"Budget references unknown metric '{metric}'.")
            continue
        actual = int(totals[metric])
        maximum = int(limit)
        if actual > maximum:
            violations.append(
                f"CI budget exceeded: {metric}={actual}, allowed maximum={maximum}."
            )
    return violations


def evaluate(root: Path, budget_path: Path | None = None) -> dict:
    report = inventory(root)
    budget_errors: list[str] = []
    if budget_path is not None:
        budget = load_budget(budget_path)
        budget_errors = budget_violations(report, budget)
        report["budget"] = {
            "path": str(budget_path).replace("\\", "/"),
            "baselineSourceCommit": budget.get("baselineSourceCommit", ""),
            "violations": budget_errors,
        }
    else:
        report["budget"] = {"violations": []}

    report["isHealthy"] = not report["structureErrors"] and not budget_errors
    return report


def print_summary(report: dict) -> None:
    totals = report["totals"]
    print(
        "CI workflow inventory: "
        f"workflows={totals.get('workflowFiles', 0)}, "
        f"dotnetBuild={totals.get('dotnetBuild', 0)}, "
        f"dotnetTest={totals.get('dotnetTest', 0)}, "
        f"dotnetRestore={totals.get('dotnetRestore', 0)}, "
        f"setupDotnet={totals.get('actionsSetupDotnet', 0)}, "
        f"checkout={totals.get('actionsCheckout', 0)}"
    )
    for error in report["structureErrors"]:
        print("STRUCTURE ERROR: " + error)
    for error in report["budget"].get("violations", []):
        print("BUDGET ERROR: " + error)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
    )
    parser.add_argument("--budget", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    root = args.root.resolve()
    budget = args.budget.resolve() if args.budget else None
    report = evaluate(root, budget)
    payload = json.dumps(report, indent=2, ensure_ascii=False) + "\n"

    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(payload, encoding="utf-8")

    print_summary(report)
    if not report["isHealthy"]:
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
