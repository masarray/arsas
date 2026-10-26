# ARSAS D7 — Four source+test-proven historical closures, P0 preservation and exact-head CI handoff
**Date:** 2026-10-26 (Asia/Jakarta). **Prior verified checkpoint:** [D6](ARSAS_RECONCILIATION_D6_2026-10-26.md) → [D5](ARSAS_RECONCILIATION_D5_2026-10-26.md). This is an **isolated maintenance audit branch**, not consumer `main`, integrated RC or released stable. Historical source/branch and initial inventory all retained.

## 1. Live paginated state at D7 exit

| Metric | D7 start | D7 verified exit |
|---|---:|---:|
| Open PR (all GET /pulls pages) | **120** | **116** |
| Open independent issues (both GET /issues pages; PR filtered) | 17 | **17** |
| Historic PRs closed in D7 | 0 | **4 unmerged**, each commented first |
| New PR / new source changes / merged PR | 0 | **0** |
| Public stable | v1.6.42 | **UNCHANGED** |

Cumulative original 154 open PR /19 issues → 116 open PR /17 issues. **39 original historical PRs closed**, offset by *one* distinct GOOSE/SGCB fix draft #753, 2 issues closed earlier with code-only truth; these counts do **not** imply integration of all features.

All source head refs checked again after actions:
- ARSAS consumer `main`: `14e5dddfbaae012b61a021366c1a864d83d316f2` — unchanged.
- ARIEC61850 `main`: `3d21238d0b62f24faecd5948a4ee238c82b35966` — unchanged.
- RC candidate #727/#729: `817c3910e8223fa2ac531a4426ada5d2d4bbbe50` — unchanged, physically gated.
- Exact #753 code/test patch head: `410a2fd3c8757f9c9cac46e2df6cb4ce047585b7` — **DRAFT OPEN, UNMERGED**, targets above RC. Existing lock.json trial engine is not engine `main`; no engine repin.
- Protected #606/#615/#753/#727/#729/#726/#716/#688/#732/#374/#435/#546/#649/#543 all confirmed OPEN.

## 2. D7 historical source carriers CLOSED UNMERGED — individual semantic proof

Each original PR HEAD is a **strict ancestor** of current RC and all original changed paths were checked at specific blobs. Ancestor alone is never the basis for closure. Original branches and blobs left intact; full per-PR comments contain **Disposition / Canonical / Unique Delta / Safety / Follow-up**.

### [#584](https://github.com/masarray/arsas/pull/584) — exact DO read and single-click navigation
[Closure comment #6413381057](https://github.com/masarray/arsas/pull/584#issuecomment-6413381057). Original head `5691e3fed31cb5dbc304ac54045759540006d362`, old→RC +1362/−0, original merge base. **All 6 files** audited:
- `MainWindow.IedBrowser.cs`: original `098f6a17860a2bbfbbe504b224c9107f8d4994a0` → RC `21a0551413a5437a562c463f011204868aab7b3e`; retains `IedBrowserTreeRow_MouseUp`, `IedBrowserDetailRow_MouseUp`, deferred association/selection-safe rendering, and exact `_iedBrowserSelectedDoReference` in `IedBrowserReadPlan.Build`. New RC strengthens with `IedBrowserExactDoFocus.IsDeclaredOwner` and gated `QueueIedBrowserFocusedRead` (read-only and bounded, but UI auto-read change explicitly requires real owner/IED acceptance).
- `MainWindow.xaml`: original `c2ee8877a94f5a63df21168a7d7f66d7591c7dd2` → RC `ee28dfe80e011740f72d114522f4311f8d852879`: both `PreviewMouseLeftButtonUp` row handlers preserved, button-origin fence remains.
- `Models/IedBrowserPresentation.cs`: original `19aa6399ddd5444522730ba1ebfc122586e6f726`→RC `1b40ab6d53f7056944be507cefebc156054c2207` keeps exact `ReadObjectReference`.
- `Models/IedBrowserReadPlan.cs`: original `0624c2190034323e8bc48eae87a409774ab46144`→RC `69bb7157e41b325f2de55a2d5e0eea75ef683bfb`; exact ordinal DataObject match retains all admissible FC Read leaves, excludes Control Operate and foreign LN, preserves Read All.
- `Models/IedBrowserStructuredDetails.cs`: original `d20ceffaa53ff711780d01bbecff22e1550b288e`→RC `c86778db18d5c2db3769dc4af3b7895e2096ee02`, still `ReadObjectReference = obj.Reference`.
- `tests/ARSAS.Tests/M214SelectedDataObjectReadTests.cs`: **identical old+RC blob** `7926caa289bcf2997db011eaa1e178d9c58a3818`, all 3 original regression methods fully retained.

### [#610](https://github.com/masarray/arsas/pull/610) — GOOSE lifetime and passive-only diagnostic
[Closure comment #6413420269](https://github.com/masarray/arsas/pull/610#issuecomment-6413420269). Head `e110c63f962b0a113a7c2bb543c0e7d4fd28ed15` old→RC +1218/−0. Four file review:
- `Models/IedBrowserGooseInspection.cs` original `0bbbc5a77c8138653475dd668f3df8a98c07c9da`→RC `2466778726d589b3e67617ad25874779f7f259a7`: retains `IsFrameCurrent` with invalid/zero TTL, future timestamp and expiry fail-closed, long-type TTL overflow safety.
- `MainWindow.IedBrowser.cs` original `1c9648bf3da414a60b6ebe333bd2665f2f714194`→RC `21a0551413a5437a562c463f011204868aab7b3e`: exact GoCB + current-wire TTL filtered from existing bounded `_latestGooseFrames` **before** unique frame selection; TTL rechecked even when MMS ledger version unchanged; expired not current; `CaptureIedBrowserGooseEvidence` passively samples at most 8 and explains TTL/identity/member rejection.
- `MainWindow.xaml.cs` original `55c484a3521fb4073b278c2ce4d693ba4d6b9e43`→RC `44e26f5ffbb4be50c56fee26d819267021d674a1`: CopyDiagnostics appends existing passive cache evidence without new capture/MMS.
- `tests/ARSAS.Tests/IedBrowserGooseInspectionTests.cs` original `6a7985b3195ea596e55327fb5192dc72d42d2bb5`→RC `70148c3f8b515d0dd5ef7cf0f7e63a067d1cb9d2`: **all eight original test methods and every Assert/InlineData retained**, plus later stricter TTL/member/identity cases.

### [#603](https://github.com/masarray/arsas/pull/603) — typed DPC double-point IEC status vs generic Enum
[Closure comment #6413462078](https://github.com/masarray/arsas/pull/603#issuecomment-6413462078). Head `b59f4951065db528d657f0f6c7ef680c6c4e01ed` old→RC +1243/−0; all five paths audited:
- `Models/IedBrowserDoublePointPresentation.cs` original `9ac4b65db122904f7b03a6898f019cda5496c21b`→RC `97672095520873b661121e08d32b9e7a83cff0f7`: presentation-only IEC 2-bit Intermediate/Open/Close/Bad from exact model-proven DPC (current RC additionally handles model-gated SV substituted states/13-bit subQ and guarded two-bit candidates). **No canonical raw SSOT ledger mutation.**
- `Models/IedBrowserSchemaIndex.cs` original `979468d162eebd78e1e8b752dea1f702778667f0`→RC `7ef8005052527d2e70e4b5228309bf4f0c172f9d`: exact declared ST stVal + DPC/Dbpos, limited XCBR/XSWI/CSWI Pos; contradictory explicit CDC fails, no arbitrary Pos or different LN.
- `Services/Iec61850ValueFormatter.cs` original `040edb34bf91d8c85bd7565db4c4a9d4b1492368`→RC `2c1646b60815c612eb111c5aaca4863adb6e2510`: no generic numeric Enum 1/2→breaker false label.
- `MainWindow.IedBrowser.cs` original `b1a512535f0227f2e41fba9194961c673d8cb337`→RC `21a0551413a5437a562c463f011204868aab7b3e`: exact observed reference/FC model gate for non-evidence field view; no Write or Process Report state overwrite.
- `tests/ARSAS.Tests/M217TypedDoublePointLiveModelTests.cs` original `c7efb4fec3053ceb6caea4fd0e90349a92a4179a`→RC `ec680b4ad8d72faf6720fac8b98057e96a9d5ba3`: **all 8 original test methods/assertions and InlineData retained**, plus additional model-gated SV/fallback cases.

### [#608](https://github.com/masarray/arsas/pull/608) — exact SGCB/GoCB initial MMS error observability
[Closure comment #6413503776](https://github.com/masarray/arsas/pull/608#issuecomment-6413503776). Old head `0034f72e636ae2a52d35fbfde4c7cec8600910e1`→RC +1225/−0. All five paths audited:
- `Models/IedBrowserControlStateEvidence.cs`: exact old+RC blob `cbaae22d81b27fbff4e14ce20a864b8f3c19a850`, retains `TryReadFailure` for only exact SGCB/GoCB model/inventory targets, never fictitious Read/GOOSE/SCL.
- `MainWindow.IedBrowser.cs` original `44275a06845ecb83598308d87eec0a9e99685266`→RC `21a0551413a5437a562c463f011204868aab7b3e`, still passes explicit/initial ledger into `TryReadFailure`; RC differentiates deferred/not attempted from actual failed/interrupted Read.
- `Models/IedBrowserReadFailureEvidence.cs` original `65f70cc9cf125183abd6f231b68bf695865d99e1`→RC `391f5c0c2a4b0673472eb4e9768277dc7548b7e3`: bounded 4096 absence samples and max-12/32 diagnostics, same association/model/epoch and exact ref/FC, no duplicate scalar store.
- `Services/DiagnosticReportBuilder.cs` original `215cad91f744ae3c9ed3ed5fb552ca0708c59282`→RC `0247655091dcc3bc83c26f77d619a91de86e4727`: `Browser ledger` + `Initial MMS errors (sample max 12)` from `InitialHydrationFailures.SnapshotCurrent`, no extra network traffic.
- `tests/ARSAS.Tests/M217DControlReadFailureDiagnosticsTests.cs`: **same blob `afecbcbc86034b3635a3bc1d325379ef86d7065a` old+RC**, all five original tests/assertions retained.

**All four PR comments added BEFORE closing; exact head/state rechecked immediately before closure. No source files edited and no obsolete PR branch deleted in this batch.**

## 3. Safety P0 source and CI result from D6 remains exact

[#753](https://github.com/masarray/arsas/pull/753) six-file corrective GOOSE full unbound allData admission + wrong-domain SGCB/GoCB initial-read fence + #606 current-tree regression tests remains **DRAFT OPEN** at `410a2fd3c8757f9c9cac46e2df6cb4ce047585b7`. [Windows/native/portable/SCL CI](https://github.com/masarray/arsas/actions/runs/40299449108) and [SCL package](https://github.com/masarray/arsas/actions/runs/40299449103) complete SUCCESS on this exact head; classifier success and reusable comparison skipped by workflow design.

Exact-six-file artifacts previously attested by D6 (not a release):
- [Portable single EXE artifact #12639857894](https://github.com/masarray/arsas/actions/runs/40299449108/artifacts/12639857894) digest `sha256:405f21003fce3394cb454c1f97b39df61d46a2f71001047364d0973271313f15`.
- [Windows sealed payload #12639887804](https://github.com/masarray/arsas/actions/runs/40299449108/artifacts/12639887804) digest `sha256:ce37084a4471324b3e2e1412b4bca636f77047c6dc0010eb584d8e89405a8674`.
- [Exact regression evidence #12640350496](https://github.com/masarray/arsas/actions/runs/40299449108/artifacts/12640350496) digest `sha256:c512379e5758d0fba9bcb1fd939d2da474e4c4bdc9bc9ca56f5cec9c702fa266`.

No required check is red on exact #753 head, but **do not merge merely from CI**: owner/reviewer acceptance of #606 7-case state (especially intentionally changed RCB collapsible visuals and passive GOOSE UI), physical C264/F650 GOOSE/SGCB wire evidence and #729 R7/R10 release gates remain separate. No physical interop claim from synthetic unit tests.

## 4. Blockers preserved — not false-closed

- **#606** unique historic 7-case test continuity: [method map](https://github.com/masarray/arsas/pull/606#issuecomment-6412633849) reviewed; corrected current-tree #753 passes CI but has not been integrated into canonical RC. Do **not** close #606 on draft CI alone.
- **#615** P0 WPF `SignalDefinition` mutable alias into native late command: [owner handoff](https://github.com/masarray/arsas/pull/615#issuecomment-6410774907). Must fix detached request snapshot and association-fenced UI in existing #726/#716 Control/Write lane with native late-flight regression; no guessed physical Operate success.
- **#599** historical expectation "last Report expanded by default" conflicts with current intentionally collapsed-by-default long report summary; no claim of exact UX parity. **#594** old RCB discovery-state provenance changed; test/asserts differ, no safe closure. **#583** CommandDockPower glyph geometry differs; GUI owner decides. **#595/#582** multi-file Files/GOOSE+UI need deeper semantic audit before closing. **#567/#570** distinct vector/phasor measurement parity needs proof.
- **#592** was not open at D7 entrance, after D6; this D7 audit did **not** perform or claim its closure. Do not double-count foreign-thread mutations.
- Active #727/#729 RC, #688 SG7, #726/#716 Control/Write, #732/UI, native/engine forks unaffected.

## 5. D8 technical priorities

1. RC owner reviews #753 six-file diff and exact seven-case #606 interpretation. Implement true current RC integration only at approved gate; rerun exact CI if source head changes. Keep RC/stable and physical qualification isolated.
2. Coordinate #615 **real source fix + late-flight deterministic tests** with current MMS Command/Write owner instead of adding overlapping branch. Preserve native single-flight/quarantine and post-Write readback.
3. Continue per-file semantic review of #595/#582 and source-level measured parity #567/#570; avoid mass stale PR closure and GUI regressions.
4. Physical FAT: verify GOOSE L2 exact publisher/TTL/allData, SGCB status, FileDirectory real IED and typed DPC against actual device, then R7/R10/acceptance signature independently. A green CI artifact is *only* test candidate.

**Result D7:** four evidence-backed administrative closures (no force-push/merge/source change), exact-head #753 CI still green, explicit owner/safety gates and next integration tasks preserved. No regression-free or physical-release claim outside tested scope.
