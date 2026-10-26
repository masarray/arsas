# ARSAS D21 → D22 engineering handoff — CI-P4 two genuine GitHub event paths qualified
**Date:** 2026-10-27 Asia/Jakarta. Continues [D20](ARSAS_RECONCILIATION_D20_2026-10-27.md). Source/change audit is separate from active runtime/safety owners.

## Final D21 hard evidence
### 1. Real code-impacting PR #757 (preserved; OPEN DRAFT; NOT merged)
- [#757](https://github.com/masarray/arsas/pull/757), base `release/arsas-integrated-rc0-20261024` at `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, feature commit `e0c9724b203ac38f2a947d71795ac1173ca5ed5b`. No source write to this commit during D21.
- Exactly 8 PR-triggered GitHub Actions runs (run attempt 1), **6 SUCCESS / 2 SKIPPED / 0 FAIL**:
  - [Build ARSAS 40407726657](https://github.com/masarray/arsas/actions/runs/40407726657): **SUCCESS**, two jobs: lightweight `pr-scope` SUCCESS; log `Changed paths=4, Windows required=true`; `Build, test, validate and package Windows application` **SUCCESS**.
  - [CI P0 Workflow Integrity 40407726690](https://github.com/masarray/arsas/actions/runs/40407726690): **SUCCESS**, includes D20's 26-case Node VM actual-workflow-expression regression within 12 Python P0 tests and multiple preexisting Python invariant suites, plus CI budget topology validation.
  - [CI P1 advisory 40407726721](https://github.com/masarray/arsas/actions/runs/40407726721): **SUCCESS**.
  - [Validate COMTRADE viewer integration 40407726689](https://github.com/masarray/arsas/actions/runs/40407726689): **SUCCESS** (independent Windows native bridge and Ubuntu shadow observer).
  - [Validate IO List Testing 40407726707](https://github.com/masarray/arsas/actions/runs/40407726707): **SUCCESS**; PR runner is **ubuntu-latest**, NOT an extra Windows compilation.
  - [Validate SV evidence bundles 40407726693](https://github.com/masarray/arsas/actions/runs/40407726693): **SUCCESS**; PR runner is **ubuntu-latest**, NOT an extra Windows compilation.
  - [Smart Discovery Field Capture 40407726714](https://github.com/masarray/arsas/actions/runs/40407726714): **SKIPPED**, including manual-regression and additional Windows `build-smart-capture`, correct for draft/unlabeled PR.
  - [Validate ARSAS Windows installer 40407726656](https://github.com/masarray/arsas/actions/runs/40407726656): **SKIPPED**.
- Exact-SHA build has **real emitted artifacts**, per GitHub Actions artifact listing: `ARSAS-windows-package-input` (sealed archive), `ARSAS-1.6.42-win-x64-portable.exe` (direct executable artifact via upload-artifact@v7 archive:false), `ARSAS-test-evidence`, source snapshot and native shadow proofs. GitHub artifact digest/size visible; **no direct binary execution/download/physical IED FAT claimed**. Candidate EXE label `1.6.42` is its version string, not a new stable v1.6.42 release.

### 2. Real docs-only PR canary #758 (CLOSED UNMERGED)
- Separate short-lived draft [PR #758](https://github.com/masarray/arsas/pull/758#issuecomment-6428567737), exact canary SHA `00b0c519107e3d3cd7560428379a22500bb78b9a`, **base the #757 feature branch** `ci/p4-failclosed-optimized-gates-d19-20261027` at e0c9724. This base matters: the protected RC itself does not contain the new CI classifier yet.
- Exactly one changed path: `docs/maintenance/D21_P4_DOC_ONLY_SCOPE_CANARY_2026-10-27.md`; no source, engine, test, workflow or release-contract modification.
- [Build ARSAS run 40410571266](https://github.com/masarray/arsas/actions/runs/40410571266): **SUCCESS** (attempt 1), **only workflow triggered on canary SHA**, `pr-scope` job 121879925336 SUCCESS, log `Changed paths=1, Windows required=false`. Canonical `build-windows` job 121879978536 **SKIPPED**. GitHub artifacts endpoint returns **empty array**: neither sealed package nor direct EXE was falsely produced.
- Canary intentionally CLOSED **UNMERGED** after evidence comment 6428567737. No change to feature branch, protected RC, main or stable. **Do not confuse successful skipped-job check with binary or physical qualification.**

## CI P4 qualification decision
**Confirmed on real GitHub events:** source/workflow change -> Windows full build; docs-only newly added Markdown -> Windows skipped; new classifier and CI-P0 proof green; draft/unlabeled Smart Capture opt-out. All those observations are from authoritative GitHub run/job/artifact APIs.
**Still missing:** repository branch-protection/required-check configuration (App read 403); genuine labeled **nondraft** Smart Capture event; real manual `workflow_dispatch` with exact SHA; doc-only downstream PR acceptance including owner policy. No need to spend extra Windows runner minutes merely to exercise expensive physical paths. **Keep PR #757 DRAFT and do NOT merge/auto-merge** until required checks and owner-qualified missing gates are verified. Historical #653 and CI-cost issue #542 remain OPEN, with D21 findings commented.

## Cost assessment / global CI topology (important distinction)
- Eight workflows are **not** eight Windows builds. Source tests show the IO/SV PR workflows use conditional `runs-on: github.event_name == 'pull_request' && 'ubuntu-latest' || 'windows-latest'`, consuming canonical exact-SHA test evidence on Ubuntu. They are integrity gate reuse and should **not** be removed as allegedly duplicated Windows compilations.
- Canonical Build ARSAS launches one Windows application job. COMTRADE integration independently launches one Windows/native bridge proof plus Ubuntu native-shadow observer when shared `.github/workflows/build.yml` changes. This second Windows native path is the remaining candidate for more precise domain-scoped/native-proof reuse (not a proven duplicate yet).
- Real docs-only canary triggered one lightweight Build ARSAS workflow, its Windows job skipped, zero artifacts. This proves a specific avoided Windows job; **no claim about account-billed minutes or global percent reduction**.
- See [issue #542 D21 cost correction](https://github.com/masarray/arsas/issues/542#issuecomment-6428607848) for domain-aware optimization constraints.

## Protected work / inventory
- ARSAS `main` `14e5dddfbaae012b61a021366c1a864d83d316f2` (verified D21); ARIEC61850 `main` `3d21238d0b62f24faecd5948a4ee238c82b35966` (D19 verified). Published stable `v1.6.42` unchanged.
- RC [#727](https://github.com/masarray/arsas/pull/727) / [#729](https://github.com/masarray/arsas/pull/729) `817c3910e8223fa2ac531a4426ada5d2d4bbbe50` OPEN DRAFT; no merge. Current RC engine trial pin `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` unchanged.
- Safety #753 `0652f66bed0c72dded20ff547e23e2ef1989341c`, #754 `2aab054b6cf9eb5500b1dce5ef5957eb8ba0232d`, #755 `7d235554225aa0341561b4e887a852fb6fd8e269`, #756 `098ec4abdf7ce8681ec97111f12f3f6151eade78` all OPEN DRAFT unchanged, inspected D21. Keep safety #606/#615, Write #726/#716, SG7 #688 and IP GUI #732 untouched.
- Final live paginated inventory **100 open PRs / 16 independent open issues**. D15-D18 **59** historical PR closed unmerged; #758 is **one separate disposable test PR** closed unmerged, do not inflate the historical cleanup figure with it.

## Next D22 meaningful task
1. Obtain owner/admin confirmation of required branch checks: read-only GitHub App protection endpoint returns 403 and empty rulesets list is insufficient. Confirm docs-only skipped job cannot incorrectly authorize an artifact-dependent release/mainline check.
2. Decide whether to review/make #757 ready for review after required-check policy; no automatic promotion or merging. Avoid rerunning six green checks or touching protected RC.
3. Audit COMTRADE native job triggers and exact-SHA artifact/CTest authority, design fail-closed reuse for non-native-impacting CI-only changes **only if equivalent**. Prefer isolated coding + targeted tests, maintain real COMTRADE/ARDIREC native FAT gates.
4. Verify optional labeled/manual Field Capture only with owner-approved bounded CI; stage preservation of source/engine pin and physical acceptance, not a checkbox.
5. Continue reducing **actual redundant cost** and cleaning stale PRs only with source-level semantic parity evidence. Keep unresolved #479 field qualification scripts, #660 public FAT prerelease policy, #594/#599 RCB operator state, #582 GOOSE safety and #520 BCUGE BRCB overflow root cause OPEN.

**D21 deliverable:** real CI evidence (two GitHub PR event types) + closed unmerged canary + issue/PR comments + this handoff. No production code changes, no safety owner interference, no release/pin/RC merge.
