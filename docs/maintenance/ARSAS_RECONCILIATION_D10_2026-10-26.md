# ARSAS D10 — historical parity closures + P0 command native ownership correction

**Date:** 2026-10-26 Asia/Jakarta. **Continue from** [D9](ARSAS_RECONCILIATION_D9_2026-10-26.md) on the same isolated maintenance/audit branch; initial D1–D8 baseline is retained. GitHub live state always supersedes the snapshot below. No baseline/main/stable/RC merge and no force push.

## 1. Live before/after state (fully paginated)

- At D10 start: **116 open PR / 17 independent issues**.
- At checkpoint after D10 actions: **115 open PR / 17 independent issues**, GET `/pulls` pages 100+15, `/issues` pages 100+32 with PRs filtered, 10+7 true independent issues.
- Exactly two historical PRs **CLOSED UNMERGED with individual source + test parity proof**: #568 and #579. One **NEW** actual P0 corrective **draft #754** opened. Accounting: 116 − 2 + 1 = **115 open**. Historic total closed unmerged now 41 (39 through D9 + 2 here), no historical branches deleted.
- ARSAS `main` `14e5dddfbaae012b61a021366c1a864d83d316f2` unchanged; engine `main` `3d21238d0b62f24faecd5948a4ee238c82b35966` unchanged; RC #727/#729 `817c3910e8223fa2ac531a4426ada5d2d4bbbe50` unchanged.
- RC's trial engine pin is still `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`, **NOT** engine main. Public accepted v1.6.42 release unchanged.

## 2. D9 GOOSE/SGCB exact-head qualification finalized

Draft [#753](https://github.com/masarray/arsas/pull/753) remains open and unmerged on `0652f66bed0c72dded20ff547e23e2ef1989341c`: **full Windows CI SUCCESS** [run 40308062610](https://github.com/masarray/arsas/actions/runs/40308062610), pinned application/native regression, packaged bridge integration, Windows single EXE publish/smoke; **SCL R7 build SUCCESS** [run 40308062583](https://github.com/masarray/arsas/actions/runs/40308062583), advisory SUCCESS. Exact-head [portable artifact 12643304566](https://github.com/masarray/arsas/actions/runs/40308062610/artifacts/12643304566). D9 FCDA matching requires exact received DataSet reference, complete ordinals and whole PDU unbound integrity. Still pending #606 seven-case GUI/GOOSE/SGCB owner review and physical device evidence. **Not a stable release**.

## 3. Two proof-backed historical tracking closures

### #568 — SGCB and GoCB inventory-proven read-only hydration
- [Proof/closure comment](https://github.com/masarray/arsas/pull/568#issuecomment-6414312792), PR head `1d094cfd8ea87f042e9256107fbcbae1b07b039d` → RC ahead1398/behind0.
- **All 5 paths examined**: `MainWindow.IedBrowser.cs` `8158c82dc7e5f24e0aa7b2fcea18e3e4731de79e`→RC `21a0551413a5437a562c463f011204868aab7b3e`; `Models/IedBrowserControlStateEvidence.cs` `aa4c07b76b285d0fd7ed37a87372eeda3dc409fd`→RC `cbaae22d81b27fbff4e14ce20a864b8f3c19a850`; `Models/IedBrowserPresentation.cs` `80505d88f12261024ec790a01d7e78b4b651b2bf`→RC `1b40ab6d53f7056944be507cefebc156054c2207`; `Models/IedBrowserReadPlan.cs` `5da450781ff0fd3aa03be5b7de7fe72fce507d09`→RC `69bb7157e41b325f2de55a2d5e0eea75ef683bfb`; original `tests/ARSAS.Tests/IedBrowserControlStateEvidenceTests.cs` blob `8e0bafd45e061626ac709e5ad0436a285a78d7a0`→RC `81873c29be0ee076d0cefc302623c2ee5404cef0`.
- All **5 original regression methods/26 assertion lines retained** and `ReadTargets/IsVerifiedInventory/TryRead` original semantic bodies identical. RC source still checks exact inventory and reads only NumOfSG/ActSG/LActTm, GoEna/NdsCom; no SGCB edit, GoCB enable, or inferred live state. RC extends truthful failure and zero-wire-time handling. Follow-up #569 SG/SE static presentation difference and #753 exact foreign-domain corrections kept OPEN. **Closure unmerged** preserves historic head.

### #579 — bounded independent multi-leaf ST DataSet values
- [Proof/closure comment](https://github.com/masarray/arsas/pull/579#issuecomment-6414314911), historic head `07b7b1820aebce84c63cf0f0f2d66cbd8cadc798` → RC ahead1373/behind0.
- All **5 paths** source compared: `MainWindow.IedBrowser.cs` `a81883ab4146abedfa242769b901962516923305`→RC `21a0551413a5437a562c463f011204868aab7b3e`; `Models/IedBrowserMultiLeafPresentation.cs` **exact same blob `3945295250fd4f6f2397161b73e38167d17f228b`** old/RC; `Models/IedBrowserPresentation.cs` `d2f8e3d94400750527bcc0ed2a3c2adca535e55b`→RC `1b40ab6d53f7056944be507cefebc156054c2207`; `Models/IedBrowserStructuredDetails.cs` `cf0f28e0193a600baa919f77a86e278483ede4d3`→RC `c86778db18d5c2db3769dc4af3b7895e2096ee02`; `tests/ARSAS.Tests/M214DataSetMultiLeafEvidenceTests.cs` **identical blob `6830be913d90f8b5f48e02cddb0503363ae26551`**.
- All **5 original tests/35 assertions byte-identical**. One canonical ST-only 2–4-leaf helper, association and offline gates, exact FC, whole-parent precedence, no fabricated atomic sample or shadow ledger. RC improves SCL structure-only fallback with ambiguous ownership fail closed. #582 SCL/GOOSE and physical acceptance remain separately open. **Closed unmerged as historical tracking only**.

## 4. Real source milestone #754 — P0 Command detached native snapshot and association-fenced GUI

**New draft [#754](https://github.com/masarray/arsas/pull/754)** from exact integrated RC, 5 files; first SHA `25cdc1764beaf47a174e8cd0b2244cbd58af2fe5`, compile correction **current head** `63434a1f9ea4502771fc2f471b9ea7859450a48f`. Distinct from GOOSE #753. **Still OPEN DRAFT, never merged.**

Changes:
1. `Services/Iec61850ControlRequestSnapshot.cs` restored historic #615 command-only snapshot exact blob `96277fceb3490a778ab0d7113fadc0bb13edee1a`; only command inputs + prior live-verified CDC transferred, no q/t/process-state authority or shared WPF alias.
2. `Services/Iec61850MonitorRuntime.cs` creates detached request **after** existing association-wide single-flight and before native `ExecuteControlAsync`; tracks nonreusable `ControlSessionIdentity` per connection and refuses changed expected association. Existing UNKNOWN/quarantine/retired flight ownership unchanged, no native Operate semantics changed.
3. `Services/UiResponsiveIec61850MonitorRuntimeFacade.cs` forwards association methods and expected token without changing frozen Write/readback or nonqueue behavior.
4. `MainWindow.xaml.cs` captures exact association before command dispatch; after await, catches and stale session results are gated `CONTROL_STALE_RESULT_IGNORED`, avoiding publication into GUI-owned signal/status/shout. Per-claim cleanup retained. No GUI layout redesign.
5. `tests/ARSAS.Tests/M217P0ControlSingleFlightRecoveryTests.cs` adds deterministic TaskCompletionSource late native mutation after caller cancellation, sticky quarantine and `NotSame` snapshot assertion, plus a source-level current GUI association gate contract.

**CI transparency:** original exact head `25cdc176...` [SCL R7 run 40312133482](https://github.com/masarray/arsas/actions/runs/40312133482) failed **one real build error**, CS0246 missing `using ArIED61850Tester.Models;` in new test file. The source application itself compiled. Corrected with targeted test-only commit `63434a1f9ea4502771fc2f471b9ea7859450a48f`; no logical scope changes. **Current exact-head CI** [Windows 40312555665](https://github.com/masarray/arsas/actions/runs/40312555665), [SCL R7 40312555609](https://github.com/masarray/arsas/actions/runs/40312555609) were IN PROGRESS at checkpoint; [advisory 40312555622](https://github.com/masarray/arsas/actions/runs/40312555622) passed. Do not attribute old parent green to new head. No redundant CI rerun manually.

**#615 remains OPEN** until successful exact-head testing and RC/Command owner code review, physical-control spot-check. No positive IED Operate or R7/R10 field evidence was invented. Only the approved RC merge gate can incorporate this candidate.

Cross-thread comments:
- [#615 explicit source + tests and gate](https://github.com/masarray/arsas/pull/615#issuecomment-6414461155)
- [#727 RC owner coordination](https://github.com/masarray/arsas/pull/727#issuecomment-6414462034)
- [#726 accepted Write freeze notice](https://github.com/masarray/arsas/pull/726#issuecomment-6414462903)

## 5. Remaining owner & safety gates (handoff to D11)

1. Verify **`63434a1...` Windows/SCL exact-head CI**, inspect test logs. If genuine new failure, fix only the caused source/test defect and rerun naturally via meaningful push; no blind full workflow waves. Check native/portable artifact only at qualifying SHA.
2. Review [draft #754](https://github.com/masarray/arsas/pull/754) with command/Write and RC owners: ensure genuine direct/SBOw control positive paths, live verified CDC, cancelled/timeout UNKNOWN, reconnect, nonreusable GUI association and operator-visible state; coordinate integration only after safety tests. #615 remains OPEN until proven accepted integration.
3. Review [draft #753](https://github.com/masarray/arsas/pull/753) six-file GOOSE + SGCB FCDA safety against exact RC and #606 seven-case GUI/state acceptance; physical GOOSE / SGCB and R7/R10 separate. Both are **independent** draft branches from same protected RC: do not blindly merge both or overwrite newer RC owners.
4. Preserve frozen #726 Write/readback, #716 Boolean editor, #688 SG7 performance, #732 IP form, #727/#729 RC gate, accepted SMV and stable v1.6.42.
5. Continue bounded historical source-delta audits (#595/#582/#567/#570/#569/#594/#599/#583) with owner behavior review; do not close on commit ancestry alone. All prior D1–D9 reconciliations and evidence comments remain intact.

**D10 deliverables:** two individual historical closures, one real five-file P0 command source/test correction in new draft PR with single explicit test-import fix, cross-lane owner comments, exact-head CI diagnostics, immutable new audit checkpoint, no stable/main/engine lock mutation.
