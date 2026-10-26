# D19: guarded CI P4 on the integrated RC (code-only candidate)
**Date:** 2026-10-27 (Asia/Jakarta)  
**Candidate branch:** `ci/p4-failclosed-optimized-gates-d19-20261027` based on exact integrated RC `817c3910e8223fa2ac531a4426ada5d2d4bbbe50` (not `main`, not stable `v1.6.42`).  
**Issue:** [#542](https://github.com/masarray/arsas/issues/542) / original historical [#653](https://github.com/masarray/arsas/pull/653).

## Purpose
Make the canonical Windows build skip **only** when GitHub's full API PR file inventory proves that every changed file is a newly added or modified `README.md` or `docs/**/*.md`, excluding `docs/INTEROPERABILITY_REFERENCE_CONTRACT.md`. All mixed, executable, test, script, workflow, engine, SCL-contract, renamed, removed, unknown, empty, API-error and truncated input means **run full Windows**. This is stricter than historical #653, because it refuses renamed files and uses the fail-closed consumer condition when the classifier job itself fails.

## Surgical changes
- `.github/workflows/build.yml`: `pr-scope` lightweight Ubuntu GitHub API read-only job (no checkout, no build); retains exactly one **existing** `build-windows` job and its exact-SHA package/test steps and artifact names. `build-windows` is skipped **only** if the classifier job completed successfully and returned `needs_windows=false` for a PR. The job runs on push/manual, on errors and missing outputs. GitHub marks skipped conditional jobs as successful: this never provides a sealed package nor physical qualification for docs-only SHAs.
- `.github/workflows/smart-discovery-capture-build.yml`: extra Field Capture Windows packaging is *opt-in* only for a **nondraft** PR explicitly labeled `ci-field-capture`; adding label triggers `labeled` event, with existing path restrictions. Manual `workflow_dispatch` still requires reusable `manual-regression` exact-SHA proof; unchanged Windows job name, source acceptance and cancellation behavior.
- `scripts/test-ci-workflows.py`: two focused static workflow policy regressions; existing P0 budget/inventory tests remain.
- This document.

## Security and merge conditions
1. This is code-only on an isolated branch. No PR has been opened automatically to avoid an unqualified multi-workflow CI fan-out; **no full Windows or physical IED FAT executed by D19**. Existing RC green CI runs predate this modification and are not evidence for this candidate.
2. Review branch-protection/ruleset required check names: GitHub's job-level skip succeeds, whereas *workflow* path-filter skips can remain pending. No change to top-level workflow trigger or required check job name. Required checks + absence of artifact for docs-only SHAs must be explicitly checked before promotion.
3. Validate true GitHub events: (a) docs-only modified PR -> cheap classifier, build skipped; (b) source/mixed/rename/contract PR -> full Windows; (c) API failure/incomplete/truncated result or failed preflight job -> full Windows; (d) manual and main push -> full Windows; (e) unlabeled Smart Discovery PR -> no extra Field Capture build; (f) label `ci-field-capture` -> field capture runs; (g) manual capture -> reusable exact-SHA proof first.
4. Preserve all canonical release/physical gates, explicit engine locks, safety #753–#756, Write #726/#716, SG7 #688, GUI #732, and RC #727/#729. Do not promote without checking real controls and branch protection.

## Offline preflight (D19)
Syntactic anchors and logic were inspected against original RC files; a nine-case conservative PR classifier truth table was evaluated in JS before the commit (docs/readme, mixed source, protected contract, source rename, removal, empty, GitHub 3000 cap, API exception). The Python regression methods are **authored but not executed on GitHub Actions** until an intentionally approved qualification run.

**No need to close #653 or #542 yet**: original issue should stay pending until on-run scenario verification, runner-minute comparison, and policy acceptance. Old PR #653 diverged and is not merged byte-for-byte.
