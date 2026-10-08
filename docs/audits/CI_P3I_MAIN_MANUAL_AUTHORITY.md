# CI-P3I — Preserve main and manual verification evidence

CI-P3H (PR #465) cancels superseded Windows-heavy pull request runs without cancelling `main` or manual runs. This follow-up closes the equivalent gap in inherited workflows which grouped by `github.ref` or used a shared `pull_request.number || github.ref` fallback.

## Why

- `smart-discovery-post-merge-production.yml` runs an independent R10 engine regression and consumes **exact main SHA** Build ARSAS evidence. A second push to `main` must not cancel the previous SHA's physical/provenance verification.
- `ci-p0-workflow-integrity.yml` audits workflow safety and budget. A new main push must not erase the previous commit's guard result.
- Five manual-capable interoperability and proof workflows previously shared a concurrency group by ref; two dispatches on the same branch could cancel one another.
- On `pull_request`, stale revisions of the **same PR** should still cancel to save resources.

## Policy

| Event | Concurrency identity | Cancellation |
| --- | --- | --- |
| Pull request | stable workflow-specific prefix + PR number | stale same-PR runs cancelled |
| Main push | stable workflow-specific prefix + unique `github.run_id` | no cross-run cancellation |
| Workflow dispatch | stable workflow-specific prefix + unique `github.run_id` | no cross-run cancellation |
| R10 post-merge push/dispatch | `smart-discovery-post-merge-production-${{ github.run_id }}` | explicitly disabled |

The top-level group is isolated per workflow. These changes affect only scheduling, not job conditions, R10 evidence, exact engine/source SHA matching, reusable regression, installer validation, portable hashes or release publication. Website deployment concurrency is intentionally outside scope.

## Validation and rollback

`scripts/test-ci-workflows.py` now locks each affected workflow's group and cancellation behavior. CI-P0 inventory/budget and the full PR workflow suite must pass before merge. For production acceptance, confirm a manual dispatch does not cancel a running post-merge verification. Roll back by restoring the pre-P3I concurrency blocks, not by changing evidence or package verifiers.
