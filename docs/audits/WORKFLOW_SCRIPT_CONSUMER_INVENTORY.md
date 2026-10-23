# ARSAS maintainability — workflow/script consumer inventory

This is an executable, review-first follow-up to [the baseline audit](MAINTAINABILITY_BASELINE_2026-09-27.md) and [issue #380](https://github.com/masarray/arsas/issues/380). It does not replace the existing architecture contract or authorize script deletion.

## Reproduce

On a repository checkout, using Python 3.11+ and Git:

```powershell
python .\scripts\test-maintenance-dependencies.py
python .\scripts\audit-maintenance-dependencies.py --root . --output maintenance-dependencies.json
```

The canonical Windows Build ARSAS workflow runs both commands against its **exact triggering Git SHA** and uploads the JSON as `ARSAS-maintenance-dependency-inventory`. The inventory is generated outside the tracked tree and does not change any application or release asset.

## What the report proves — and does not prove

- Lists Git-tracked workflows, top-level trigger types, Git-tracked scripts and **literal textual references** from workflow and other source/documentation files.
- Separates direct workflow references from other tracked references; stores a source commit so the map can be compared across candidates.
- `requiresManualConsumerReview` means **no literal reference was found in scanned tracked text**. It does **not** mean an entry point is orphaned or safe to delete. Dynamic invocation, branch-specific scripts, workflow_dispatch, local/operational calls, generated jobs and external consumers may not be visible.
- Neither keyword matching nor a successful build proves ownership of an asset, a security boundary, or the absence of historical callers. Complete those reviews separately.

## Deletion/refactor gate for each candidate

1. Identify its owner and actual inputs/outputs, event trigger(s), exact call sites, permissions and side effects (including Git tags, Releases, Pages, SBOM and evidence writes).
2. Check manual and out-of-repository uses with the maintainer, archived references, active workflow branches and release provenance; retain unknowns rather than treating them as zero consumers.
3. If retiring an entry point, remove it together with all **verified** callers, documentation and tests in one bounded PR. Do not alter historical tags/artifacts or silently weaken required checks.
4. Run exact-head source clean, consumer tests, full Windows build, portable smoke and post-merge verification. Changes to live IEC 61850 paths require separate targeted physical evidence.

## Implementation order

P1: classify the report's consumers and unknowns, plus review existing asset-provenance dispositions. P2: eliminate only verified dead automation and duplicate source-of-truth declarations. P3: extract one pure tested semantic/persistence component at a time. P4: characterize facade/core/client session lifecycle deterministically before moving ownership. P5: simplify UI projection without shifting protocol truth into WPF. Stage main protection after checking bot writers and recovery access.

The v1.6.40 accepted release and its engine lock are not a pending bug fix under this maintenance program.
