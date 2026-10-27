# ARSAS D26 → D27 — CI-P3J exact PR native-shadow run identity

**Date:** 2026-10-27 WIB. Continues [D25](ARSAS_RECONCILIATION_D25_2026-10-27.md). Concrete code milestone on protected integrated RC, no main/RC/stable mutation.

## Completed engineering action

- Created isolated D26 source commit [`2c95d0c49d6d04ded18fa39bd4e431b85fa81e67`](https://github.com/masarray/arsas/commit/2c95d0c49d6d04ded18fa39bd4e431b85fa81e67).
- Opened [PR #760](https://github.com/masarray/arsas/pull/760) as **OPEN DRAFT, NOT MERGED**, base `release/arsas-integrated-rc0-20261024` at exact `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`.
- Changed only `scripts/compare-ci-native-shadow.py`, `scripts/test-ci-native-shadow.py`, and `docs/audits/CI_P3J_D26_NATIVE_PR_RUN_IDENTITY.md`.
- Discovered existing native shadow chooser matched canonical GitHub Build ARSAS by branch + head SHA, but two PRs can share them while targeting different synthetic merge trees (e.g. RC #727, main #729). New code requires exact PR association via the canonical workflow run's `pull_requests[].number`; exact PR identity is read from existing runner `GITHUB_EVENT_PATH` and cross-checked to passed head SHA and branch. No workflow YAML changes or fallback based only on branch.
- The source merge SHA, engine/native locks, CTest inventory, sealed archive/DLL SHA-256, canonical run/attempt freshness and authority flags stay enforced. **`deduplicationAuthorized=false`**, **`independentWindowsCTestRetained=true`**, **`releasePromotionAuthority=false`**.
- Added five offline unittest methods, from 15 to 20.

## Observed hosted CI (snapshot during CI, check latest results before any promotion)

- [CI P0 Workflow Integrity #40463264351](https://github.com/masarray/arsas/actions/runs/40463264351): **COMPLETED SUCCESS**, including `Ran 20 tests in 1.018s / OK` for native shadow.
- [Validate COMTRADE integration #40463264264](https://github.com/masarray/arsas/actions/runs/40463264264): required independent Windows native CMake/CTest job **COMPLETED SUCCESS**; advisory Ubuntu native comparison was **IN_PROGRESS** at this checkpoint.
- [Build ARSAS #40463264271](https://github.com/masarray/arsas/actions/runs/40463264271): canonical Windows build **IN_PROGRESS** at checkpoint, no final artifact/release assertion.
- Exactly one PR/event change wave, no manual workflow reruns.

## Cross-thread baseline

Current checked source refs:
- ARSAS main `14e5dddfbaae012b61a021366c1a864d83d316f2`.
- ARIEC61850 main `3d21238d0b62f24faecd5948a4ee238c82b35966`.
- Integrated RC #727/#729 `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, still OPEN DRAFT; trial engine pin unchanged.
- #759 `43b7eed442713194f76ea115ce3bf5cecdc6f5cf` OPEN/READY; #757 `e0c9724b203ac38f2a947d71795ac1173ca5ed5b` OPEN DRAFT.
- Full changed-filename audit reports **zero identical changed-file paths** between #760 and #757, #759, #753–756 safety, #726/#716 Write, #688 SG7, #732 UI, or #727/#729 RC. No guarantee of semantic integration from file-level comparison alone.
- GitHub open PR inventory increased by one to **102** from D25's 101; 16 independent issues previously observed. No historical PRs closed/merged.
- Owner branch-protection policy still not shown through managed GitHub App (403 in earlier reads), so approval/required-check evaluation is outstanding.

## D27 gating sequence

1. Verify the final exact-SHA canonical Windows #40463264271 and COMTRADE #40463264264 conclusions, including observer log on matching source/PR number, canonical run and artifact proof. If failing, repair root cause in a single targeted source wave; never rerun already-green tests without a reason.
2. Inspect new human/Codex reviews; retain #760 DRAFT pending owner required-check review. Do **not** merge into RC or main automatically.
3. Keep independent COMTRADE Windows native acceptance; test parity is advisory and cannot justify deleting runner until separate formal equivalence/authority.
4. Progress CI cost #542 with actual runner/job minutes, not workflow-count assumptions; #757 docs-only gates remain isolated. Do not alter engine pin or release.
5. Preserve physical IEC 61850 FAT and wire/PCAP acceptance limits for #446/#520, and historical replacement #479 until #759 is actually integrated.

**Software regression verification is not FAT/field authority.** No physical device test, stable publication or engine repin was performed at D26.
