# ARSAS D4 reconciliation checkpoint — 2026-10-26

Prior: [D3](ARSAS_RECONCILIATION_D3_2026-10-26.md). All refs and counts were reverified using complete live pagination after mutation; this document exists only on the isolated audit branch, not on main.

## 1. Live state and protected boundaries

| Metric | Enter D4 | Exit D4 |
| --- | ---: | ---: |
| Open PR | 129 | **125** |
| Open issue excluding PR | 17 | **17** |
| D4 PR closed unmerged | 0 | **4** |
| New D4 PR | 0 | **0** |
| RC/Main/Engine code changed by D4 | no | **no** |

Consumer main SHA 14e5dddfbaae012b61a021366c1a864d83d316f2; engine main SHA 3d21238d0b62f24faecd5948a4ee238c82b35966; protected RC #727/#729 SHA 817c3910e8223fa2ac531a4426ada5d2d4bbbe50; accepted stable remains v1.6.42.

Original inventory 154 PR/19 issue => now **125 PR/17 issue**; cumulative 30 historic PR closed and two issues closed, offset by one new corrective draft #753. All active #753/#606/#615/#729/#727/#726/#716/#732/#688/#374/#435/#546/#649/#543 preserved OPEN. No source released.

## 2. Four administrative closures backed by source and regression equivalence

- **[#588 Files workspace](https://github.com/masarray/arsas/pull/588#issuecomment-6410699978):** closed unmerged. Old head 2001a359a1cb0e93e80fd4272308257b62545b78 ancestor RC ahead 1343/behind0. Original two files: old UI blob e94cffacf966c3c38bddc61afcc37605cf808297 -> RC 21a0551413a5437a562c463f011204868aab7b3e; original Files click handler entirely identical and selects/renders Files before directory Read. Original tests blob 9ed517c6ad5dfed3ca22f39c3781418bb519281e -> RC e39a811804ee359e4a94766b38c5492a3be9b8ea; every old method/assertion retained with additional COMTRADE/directory coverage.
- **[#596 GOOSE SCL VLAN/time](https://github.com/masarray/arsas/pull/596#issuecomment-6410702296):** closed unmerged. Old head 5bf72735c85857d6fc5b6559d1f64845af720615 ancestor RC ahead 1281/behind0. Three paths: GooseInspection old blob f3178dd6446514a7f0a0a97ecf94a376bb04b747 -> RC 2466778726d589b3e67617ad25874779f7f259a7 (original SclNetworkProperties full body identical); MainWindow Browser old 3cfff7f850c1aed1ae3c7c6d078aaf41755439cd -> RC 21a0551413a5437a562c463f011204868aab7b3e (same trusted SCL attribution and Not read fallback; RC adds TTL safety); IedBrowserGooseInspectionTests old feccae4fdca4c93523db053f69fbf8817cce438b -> RC 70148c3f8b515d0dd5ef7cf0f7e63a067d1cb9d2 (all original tests/assertions present).
- **[#590 FC CO command-only](https://github.com/masarray/arsas/pull/590#issuecomment-6410743195):** closed unmerged. Old head 6b2c42ee7d3340cc4ea297dc95d980a36d7ebd72 ancestor RC ahead 1316/behind0. Verified all four paths: ReadPlan.UnobservedValue only renders 'Control only' for FC CO, otherwise 'Not read'; StructuredDetails and MainWindow continue invoking it with advisory tooltip and no MMS Operate. Test M214IedBrowserControlOnlyReadStatusTests exact same blob 4c8ed3fd3d07968d3fa2b3dd01e6e93f8c89767e on old + RC.
- **[#587 SGCB LActTm UTC](https://github.com/masarray/arsas/pull/587#issuecomment-6410745341):** closed unmerged. Old head 5ef905ecefbe1a3bccbec28dfc89f71f9f374a15 ancestor RC ahead 1345/behind0. Verified all five paths: FormatObservedField only yields 'Not initialized' for SGCB LActTm when decoded, wire-proven *zero seconds and zero fraction*; LiveValues recognizes .LActTm as time; NativeIec61850Client decodes .LActTm/$LActTm only on actual raw MmsDataKind.UtcTime; MainWindow uses proven format with stale/Read attribution. Regression M214SgLastChangedWireEvidenceTests exact same blob 7d0fafd60c54e0be6c11c6cefd33d5c4c420d7b7 old + RC.

Every PR has an individual 5-part disposition, exact head+old/RC test blob audit and original source branch retained. None of these administrative closures is a physical IED qualification.

## 3. #753 complete exact-head CI — code remains draft

[PR #753](https://github.com/masarray/arsas/pull/753), head d0fbb5c2ae9f69f56badbba9bef5e15d66a78dc7, targets RC 817c3910e8223fa2ac531a4426ada5d2d4bbbe50 and changes exactly five read-only GOOSE/SGCB files. No merge. [Windows CI run 40280841713 / job 121433555670](https://github.com/masarray/arsas/actions/runs/40280841713/job/121433555670) COMPLETED SUCCESS; all 36 job steps checked. Exact-SHA source/engine/native lock, dotnet restore and build, pinned engine regression, ARSAS full application tests, canonical test attestation, native CTest, native managed integration, portable single EXE publish/smoke, sealed Windows package input and artifact uploads all succeeded. [SCL package](https://github.com/masarray/arsas/actions/runs/40280841715/job/121433555800) and source area classifier also succeeded. Reusable build comparison was SKIPPED by workflow design, not a failure.

Exact-head artifacts (not accepted stable release or physical qualification):
- [Windows portable single EXE artifact 12632069809](https://github.com/masarray/arsas/actions/runs/40280841713/artifacts/12632069809): artifact sha256 230ee1c860d4ff615fa5bb3b5f0137bf41cc2f95ed6a41925065fe3b0aa6fc32.
- [Windows sealed package input 12632084670](https://github.com/masarray/arsas/actions/runs/40280841713/artifacts/12632084670): artifact sha256 9cceef23895f85de0d8c49afb3fde0030a72faa5ca35b26fd028abb8300292e7.
- [Test evidence artifact 12631478918](https://github.com/masarray/arsas/actions/runs/40280841713/artifacts/12631478918).
- [Native CTest evidence artifact 12632485527](https://github.com/masarray/arsas/actions/runs/40280841713/artifacts/12632485527).

#753 still needs current RC owner review/targeted integration, independent GOOSE capture/FAT and #606 legacy seven-case coverage disposition. CI alone is not R7/R10 physical release signoff.

## 4. #615 P0 Command-owned mutable alias — exact current-RC handoff

[New explicit owner handoff #6410774907](https://github.com/masarray/arsas/pull/615#issuecomment-6410774907). Protected RC currently aliases WPF SignalDefinition directly into command request, then dispatches SAME object to native client which mutates request.Signal ControlSemanticVerified/model/status/value after await, potentially after caller timeout. Existing single-flight/quarantine controls MMS admission but does not stop stale GUI mutation. Existing RC lacks Iec61850ControlRequestSnapshot and association-fenced UI publication. Historical #615 holds candidate snapshot, but diverged dirty branch cannot be blindly cherry-picked.

Owner acceptance tests: (1) immutable-to-GUI detached native snapshot even after late completion; (2) old association result must be ignored after reconnect without false 'not sent' claim; (3) native-completed UNKNOWN wire outcome quarantines; no new command to IED until proven safe. Coordinate actual source edits with #726/#716 MMS Write/Control. No code on this lane was changed by reconciliation D4.

## 5. Deferred (not falsely closed)

#599 RCB collapsible test evolved with distinct flag placement; exact assertion parity failed. #591 MMS Read policy became stricter typed TryAcceptVerifiedMmsString; original assertions changed, requires deeper source proof. #586 six-path FileDirectory + transfer semantics require full path audit. #567/#570 phase/vector semantic deltas still unproven. Historic #606 stays OPEN even after #753 CI until actual merge/acceptance. Stable, engine pin, physically gated #729 stay isolated.

**Next:** integrate/review #753 safely when authorized by RC owner; assess full #606 assertion coverage; fix #615 in active command lane with deterministic tests and new canonical CI. Do not mass close, do not force push, do not change v1.6.42, do not claim physical R7/R10 from automated tests.
