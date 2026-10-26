# ARSAS D11 — exact-head CI closure, integrated safety rehearsal and bounded historical audit

**Date:** 2026-10-26 Asia/Jakarta. **Source of truth:** [D10](ARSAS_RECONCILIATION_D10_2026-10-26.md), [D9](ARSAS_RECONCILIATION_D9_2026-10-26.md), D8–D1. This checkpoint is created on the pre-existing *isolated audit branch* only; protected RC, consumer main, engine main, v1.6.42 release, and field qualification authority unchanged. D11 creates no historic closures and never deletes historical branches.

## A. Live, fully paginated inventory and ownership

Initial D11: **115 open PR / 17 independent issues**, checked GitHub pulls page 100+15 and issues 100+32, excluding issue list entries containing `pull_request`. After opening meaningful integration-only draft #755: **116 open PR / 17 independent issues**, pulls 100+16, issues 100+33 (9+8 independent issues). Arithmetic: 115 + one safety integration draft = 116; zero historical closures in this batch. D1–D10 total remains **41 historic PRs closed unmerged** with source-and-test evidence.

Frozen SHA checks on 2026-10-26:
- ARSAS consumer main: `14e5dddfbaae012b61a021366c1a864d83d316f2`.
- ARIEC61850 engine main: `3d21238d0b62f24faecd5948a4ee238c82b35966`.
- Protected #727/#729 integrated trial RC: `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, still DRAFT, with trial engine lock `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` (not engine main).
- Accepted stable **v1.6.42 preserved**.
- #726 frozen Write/readback `42c7cfe1364faaeeee64180bd82e5dbef4f7355a`, #716 Boolean Write `f53857d5f66d55d80b0f4d04eec91df5fc48053d`, #688 SG7 `774321313aac96ec87a4f24edf61731d032237e6`, #732 IP/UI `c47a622037110a09ed1e16653aac0c44b5f40de2`; none touched.

## B. D10 P0 native ownership milestone has FULL exact-head CI evidence

Draft [#754](https://github.com/masarray/arsas/pull/754) exact HEAD `2aab054b6cf9eb5500b1dce5ef5957eb8ba0232d`, 5 files vs exact RC:
- [Windows workflow 40313104906](https://github.com/masarray/arsas/actions/runs/40313104906): **completed SUCCESS**, app regression tests, immutable engine/native lock, native bridge CTest, managed packaged native bridge integration, portable EXE publish+smoke, package seal.
- [SCL workflow 40313104592](https://github.com/masarray/arsas/actions/runs/40313104592): **completed SUCCESS**.
- [Exact Windows portable artifact 12645907095](https://github.com/masarray/arsas/actions/runs/40313104906/artifacts/12645907095), CI candidate only, **not a replacement of public stable**.

P0 change remains isolated: `Iec61850ControlRequestSnapshot` detached command-only SignalDefinition, non-reusable `ControlSessionIdentity` verification through inner runtime/facade, GUI result-and-exception publication fencing, deterministic simulated late flight and sticky native UNKNOWN quarantine tests. The first two commits failed a missing test import and an operator-display string assertion; they were corrected in HEAD `2aab054` and **the exact corrected HEAD is green**. There is NO physical Operate field/IED proof; [#615](https://github.com/masarray/arsas/pull/615) remains OPEN until owner code review and actual RC integration.

Draft [#753](https://github.com/masarray/arsas/pull/753), head `0652f66bed0c72dded20ff547e23e2ef1989341c`, independently passed Windows [40308062610](https://github.com/masarray/arsas/actions/runs/40308062610) and SCL [40308062583](https://github.com/masarray/arsas/actions/runs/40308062583). GOOSE allData and SGCB exact inventory verification remains safety-isolated. [#606](https://github.com/masarray/arsas/pull/606#issuecomment-6412633849) historical 7-case visual/field acceptance is still required.

## C. Real D11 engineering progress — 11-file combined safety rehearsal

NEW draft [#755](https://github.com/masarray/arsas/pull/755), exact HEAD [`a0b96118d1ba4f04e108ff1fb9321f81fd653146`](https://github.com/masarray/arsas/commit/a0b96118d1ba4f04e108ff1fb9321f81fd653146), 1 commit ahead/0 behind protected RC `817c391`. Purpose is **compile+test a combined safety candidate without merging either source PR**; open draft, not release qualified.

### Verified deterministic merge design
- #753 exact 6 changed files; #754 exact 5 changed files.
- Both source branches compared to same RC SHA, each behind=0; all **11 paths are disjoint**, overlap=0; original changed files were all fetched by exact source commit/HEAD and their Git **blob SHA**, with no generated/adapted source.
- Branch `integration/d11-goose-command-safety-rehearsal-20261026` created at RC. A single tree commit `a0b96118` overlaid EXACT 11 source Git blobs on RC base tree `257abcdb4f7b184458c4d24ee9e61c3f043c97bb`, resultant tree `91746aca8eafd783aeaafac4af4446044d319fd2`; branch advanced with exact expected-parent SHA, no force-push and no blind cherry-pick.

**#753 6 blobs:**
- `MainWindow.IedBrowser.cs` `5a03abc5cf8e35fc87a8b3fde9e9e21091e69d75`
- `Models/IedBrowserGooseInspection.cs` `d3fe77ffa3d00a3d09d94e77cd4aa305d0acf0ad`
- `Models/IedBrowserReadPlan.cs` `0cc15916cb050f664b2fdef44b78bc9c572cc117`
- `tests/ARSAS.Tests/IedBrowserControlStateEvidenceTests.cs` `1218bd76fc8283c5a9c164bbc03a34c2d434ba63`
- `tests/ARSAS.Tests/IedBrowserGooseInspectionTests.cs` `f734c2590dc583d44e9bea9e3a38a1bcbdb2ef82`
- `tests/ARSAS.Tests/M217DCurrentTreeEvidenceContinuityTests.cs` `b2db7c9a1cdab0ad8b9465fff1f148ddd09ea661`

**#754 5 blobs:**
- `MainWindow.xaml.cs` `f6447310afee0e79e32d26a1e4c685a09a9bb8f5`
- `Services/Iec61850ControlRequestSnapshot.cs` `96277fceb3490a778ab0d7113fadc0bb13edee1a`
- `Services/Iec61850MonitorRuntime.cs` `110fd3a115bbebdbc9c939150f281103294c9086`
- `Services/UiResponsiveIec61850MonitorRuntimeFacade.cs` `f1ecb9f9713448f4694965930cc7afa807c79026`
- `tests/ARSAS.Tests/M217P0ControlSingleFlightRecoveryTests.cs` `5906e531e9bc097d155405396ed5db9c42bd20b7`

**Only one combined CI workflow wave**, due to substantive source integration. [Windows/native/portable workflow 40318195357](https://github.com/masarray/arsas/actions/runs/40318195357) and [SCL R7 40318195452](https://github.com/masarray/arsas/actions/runs/40318195452) **in progress at creation of this checkpoint**; [Reference Guard 40318195336](https://github.com/masarray/arsas/actions/runs/40318195336) and [P1 advisory 40318195335](https://github.com/masarray/arsas/actions/runs/40318195335) **completed SUCCESS**. Recheck exact final run conclusions before declaring combined qualification. No manual reruns performed.

[RC owner coordination comment](https://github.com/masarray/arsas/pull/727#issuecomment-6415364611). **No merge allowed without #606 7-case GOOSE+SGCB owner GUI/physical acceptance, #615 command Control owner review and physical timeout/reconnect/Operate spot test, #727/#729 RC integration owner acceptance**. Protect Write #726/#716, SG7 #688, IP Form #732, accepted SMV and v1.6.42; green synthetic CI alone is not R7/R10 physical qualification.

## D. Historical audit #577: semantic hold, NOT closure

[Individual #577 audit comment](https://github.com/masarray/arsas/pull/577#issuecomment-6415341749). Original historical SHA `4be7b51b9040a7a5f1d8ecaa1d90bbc1a3b9d081` vs exact RC ahead1374/behind0. All six original paths examined:
- `Models/IedBrowserEngineeringComposite.cs` `48ba62366bc2240b2243df34367d78e5164da8cf` → `90c19fe2ff52c2c0bda6ae2379165bba20acd792`.
- `Models/IedBrowserEngineeringObjects.cs` exact SAME blob `2b5ec785c3c82e4a290849f59476c4bc4f45332a`.
- `Models/IedBrowserPresentation.cs` `d572460a37e14dd1ea86ee51321f7948029b9c25` → `1b40ab6d53f7056944be507cefebc156054c2207`.
- `Models/IedBrowserStructuredDetails.cs` `5d7cf4ae48d4d51d7df25a4bf75c9c83b3243f3e` → `c86778db18d5c2db3769dc4af3b7895e2096ee02`.
- `tests/ARSAS.Tests/M214EngineeringHierarchyUxTests.cs` `b76a7d3c6f4894f315c4e241ae6d1bea819381a0` → `fbf25c0fcd58e02946ff8aff27be9b4d3623ea28`: 4/4 test method names retained; 27/28 original Assert lines identical; lost `angle 0 (independent, not paired)` wording.
- `tests/ARSAS.Tests/M214MergedCfAndVectorAuthorityTests.cs` `5ee9b1e378659822a6f725a1e3e7aa897a7ec43f` → `e4f8c64cebb02d93719f151b46082d82f40de4e2`: 3/3 methods retained; only 33/36 exact assertions, differing unpaired angle/timestamp semantics (`∠ 30`, `Independent`).

**Disposition:** KEEP OPEN. Canonical current RC preserves scalar MV, exact typed grouping, and schema authority; historical unique policy that withholds glyph from mismatched time not proven equivalent to newer explicit provenance. Related #570/#567 need measurement GUI owner. Do not relabel independent angle as atomic, fabricate q/t, or rollback current UX without field decision.

## E. D12 action list and stop gates

1. Verify exact-head **#755 `a0b96118...`** Windows managed+native/portable and SCL run conclusions; if fail, inspect specific job logs and make minimal justified correction only on rehearsal branch (do not touch #753/#754 source without owner synchronization).
2. If combined CI green, review 11-file integration PR and secure owner signoff for #606 7-case GOOSE/SGCB, #615 Command late-flight/Operate, #726 Write frozen scope, #727 RC. Keep #753/#754/#755 draft until accepted. No upgrade stable or physical FAT claim.
3. Consider targeted phasor semantics decision across #577/#570/#567 and RCB state #594/#599, then multi-file #595/#582; no mass closure, each requires disposition → canonical replacement → unique delta proof → safety → follow-up.
4. R7/R10 physical safety cannot be inferred from synthetic test data, and no MMS Report/Quality/Timestamp/GOOSE FCDA may be manufactured.
