# ARSAS D5 — current-tree M2.17-D test recovery and three evidence-backed PR closures
**2026-10-26 (Asia/Jakarta, afternoon)** · Previous source of checkpoint continuity: [D4](ARSAS_RECONCILIATION_D4_2026-10-26.md), [D3](ARSAS_RECONCILIATION_D3_2026-10-26.md), [immutable historical inventory](ARSAS_OPEN_PR_ISSUE_AUDIT_2026-10-26.md) on its original audit ref. This checkpoint is on the **isolated maintenance branch**, not production `main`.

## 1. Live verified state after D5

| Measure | D5 beginning | D5 latest |
| --- | ---: | ---: |
| Open PRs (both paginated pages, live) | **125** | **122** |
| Open issues excluding PRs | 17 | **17** |
| Historical PRs closed during D5 | 0 | **3, all unmerged** |
| New PR | 0 | **0** |
| Real existing draft safety PR #753 | 5 files, earlier green SHA | **6 files + 3 new targeted tests on new SHA**, exact-head CI running |
| Accepted public stable | v1.6.42 | **untouched** |

Consumer main `14e5dddfbaae012b61a021366c1a864d83d316f2`; engine main `3d21238d0b62f24faecd5948a4ee238c82b35966`; RC #727/#729 target `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`: **all unchanged** after D5. No engine repin, forced merge, branch reset/delete or fresh public release. Initial inventory **154 PR/19 issues → 122 PR/17 issues**; cumulative **33 historical PR closures** (offset by newly created safety #753 in D3) and **2 issue closures**. Protected P0/active #606/#615/#753/#727/#729/#726/#716/#732/#688/#374/#435/#546/#649/#543 all confirmed OPEN.

## 2. Three independently adjudicated historical source paths

All closures have individual **Disposition / Canonical / Unique Delta / Safety / Follow-up**, exact original head, ancestor compare, every touched file examined for preserved behavioral semantics, and tests. These are historical tracking closures **unmerged**; their branches and original blobs are retained for reversibility.

### [#586](https://github.com/masarray/arsas/pull/586) — real FileDirectory includes ordinary MMS folders, never arbitrary download
[Full per-PR evidence/comment #6412537526](https://github.com/masarray/arsas/pull/586#issuecomment-6412537526). Original head `3bdeff82affcb267ba166302e124c17d8df58b44` ancestor RC +1351/-0, original merge-base exact old head. Reviewed all six PR paths:
- Original and RC `Services/FaultRecordTransferClient.cs` **same exact blob `52ec1ad79af402aea0ba9fad8db1d9c319f42e98`**. Reuses one ARIEC backed MMS association, paginated root directory max 32 pages/20,000 entries, cancellation.
- Evolved `IedBrowserFileCatalog.cs` old `440049af8665c4cf079db1f3f2738caac8adf478` → RC `6fa4ffbd4fb6c2aa3b915a7c6535441ea872d133`: retains ordinary root entries/duplicate suppression and exact **COMTRADE CFG+DAT record download eligibility**, adds one physically verified bundle display row.
- Evolved `IedBrowserRemoteFile.cs` old `52a4253cd03eb4fb4103ea99d2094dab5c4416fd` → RC `826cbe4086eb7075e77914bbde0caac8cad0b224`: directory `Folder` signal preserved.
- Evolved `MainWindow.IedBrowser.Files.cs` old `90a530894df2eb42a1e9350028a52f803c8e5735` → RC `d207f127d3dbb8f05afe1974c3ae7ca8ea3d5949`: exact `selected is { IsDirectory: false }` and `TryGetRecord` guard unchanged.
- Evolved `MainWindow.IedBrowser.cs` old `dc5c2f385610bbbdb0c15fdb94e085c23c788ea7` → RC `21a0551413a5437a562c463f011204868aab7b3e`: root read now first, shows early partial truthful inventory, optional bounded recursive enrichment, current selected/association/CTS guards.
- `tests/ARSAS.Tests/M2IedBrowserFilesTests.cs` old `e4ecbaafa3dc1a7abb3acee4f808b1b5f68397f1`→RC `e39a811804ee359e4a94766b38c5492a3be9b8ea`: all historical tests/assertions retained, additional record bundling tests. No inferred downloads.

### [#591](https://github.com/masarray/arsas/pull/591) — MMS Read verified scalar fence strengthened
[Full evidence/comment #6412540329](https://github.com/masarray/arsas/pull/591#issuecomment-6412540329). Original head `c4a959b0fcd38a8467232f8428288144b3afb48b` ancestor RC +1310/-0. Six touched files adjudicated. **Entire old `IedBrowserReadValuePolicy.TryAcceptDisplay` body preserved verbatim**: still rejects raw MMS unknown BER, untyped parent Structure/Array, unresolved placeholders, overlong text. Current RC adds safe typed `TryAcceptVerifiedMmsString` exception for bracket-prefixed **literal** only with verified MMS primitive wire kind and exactly compatible live model string type; safe zero-length Octet/VisibleString require wire proof. Isolated reader, WPF current-association gate, canonical ledger all use strengthened typed verdict; shared passive monitor retains conservative old `TryAcceptDisplay`. All substantive original value/ledger regression test methods and assertions persist; historic fourth textual caller check intentionally evolves from old helper to typed authority. No guessing scalar value/quality.

### [#593](https://github.com/masarray/arsas/pull/593) — root MMS FileDirectory before COMTRADE enrichment
[Full evidence/comment #6412543518](https://github.com/masarray/arsas/pull/593#issuecomment-6412543518). Old head `9fbb92eb9184944153e687118fd8b41196bae273` ancestor RC +1299/-0. Three touched paths audited. `IedBrowserFileDiscoveryPlanner.cs` old `bdf53ac19821dddeedd58634cefd433842438b4b`→RC `85e46d4beb1d8a2f796a6e5b2b79e44dc660f000`, old root first / optional 30-second bounded folder scan / early UI publication / exception honest fallback remain, while current source adds physically observed **RIO sidecar** admission only to exact engine-proven record. Browser `MainWindow.IedBrowser.cs` old `f3c107b8180bcb0dcaf4e35be392edcb7dd5df1f`→RC `21a0551413a5437a562c463f011204868aab7b3e` uses same owner/CTS/selected-device gates. `tests/ARSAS.Tests/M214RootFirstFileDirectoryTests.cs` **exact identical blob `1a9ea238c9f0e665fd72b8f22cd3276911c8d34b`**, all original test cases.

## 3. Actual further code milestone: three #606-specific current-tree regression tests

Draft [#753](https://github.com/masarray/arsas/pull/753) now has new **test-only** commit `410a2fd3c8757f9c9cac46e2df6cb4ce047585b7` (parent successful 5-file SHA `d0fbb5c2ae9f69f56badbba9bef5e15d66a78dc7`). Branch `safety/m217d-raw-goose-sgcb-admission-20261026`; six changed files versus protected RC, compare **ahead6/behind0**; 3 source + 3 tests. New test file `tests/ARSAS.Tests/M217DCurrentTreeEvidenceContinuityTests.cs` (read-only/synthetic, no native calls) adds:
1. `ExactWireGoCbCanBeReadUnboundWithoutAnMmsDataSetInventory`: exact received full GoCB, no expected DatSet or publisher label, preserves ordered unbound values and zero claimed IEC FCDA/FC, rejects empty/wrong GoCB, APPID mismatch and ambiguous frame.
2. `ExactLiveSgcbInitialReadIsBoundedReadOnlyAndSkipsObservedFields`: 3 legitimate SGCB `SP` leaves, excludes EditSG/CnfEdit, observed `ActSG` read reduces pending 3→2, maxTargets1 and offline suppress action.
3. `UnverifiedSclOrWrongFcNeverBecomesAnInitialSgcbRead`: refuses SCL-only, wrong FC and zero-budget initial SGCB read.

Earlier #753 D3 code contains actual no-foreign-LD domain fence, strict whole unbound GOOSE vector check and WPF display callsite. New test-only commit **triggered new exact-head CI**:
- [Windows run 40299449108](https://github.com/masarray/arsas/actions/runs/40299449108) status **IN PROGRESS at checkpoint**, do NOT claim new head green.
- [SCL run 40299449103](https://github.com/masarray/arsas/actions/runs/40299449103) **IN PROGRESS at checkpoint**.
- Classifier succeeded; reusable test comparison skipped intentionally. Old 5-file exact-head [run 40280841713](https://github.com/masarray/arsas/actions/runs/40280841713) **fully successful** for full .NET Windows, native CTest, portable single EXE/smoke, but does NOT prove six-file head. Draft PR metadata/body was updated to current six-file SHA/CI; kept DRAFT/OPEN. No unauthorized RC merge.

[Historical #606 complete seven-test map, with two deliberately transformed UI assertions](https://github.com/masarray/arsas/pull/606#issuecomment-6412633849) was published. The seven legacies are tracked individually: (1) exact GoCB without MMS DatSet now new test; (2) negative wrong publisher current tests; (3) malformed unordered raw GOOSE D3 new guard/test; (4) initial SGCB 3→2/budget now new test; (5) SCL/FC/foreign domain now new+D3 tests; (6) report flags are now separate visual sections **nested under RCB control attributes through `IedBrowserDetailSectionLayout.Attach`**, not literal old raw projection; (7) in-place subscriber shares `StartGooseSubscriber_Click` and RCB uses `IedBrowserRcbCheckLease.IsCurrent`, not outdated source text. No #606 closure until owner verifies UI parity, exact-head full CI, reviewed RC integration, and separate physical signoff.

## 4. Current unresolved engineering blockers and preserved boundaries

- **#615 P0** native control late-reply UI-alias hazard: currently unresolved in protected RC; prior [owner handoff](https://github.com/masarray/arsas/pull/615#issuecomment-6410774907) proves source path and required snapshot/association-fenced UI/unknown outcome regressions. Do NOT step on existing #726/#716 Control/Write owner. No code changed on #615 by D5.
- **#599 RCB section/UI** remains OPEN: old raw projection asserted nested flags, but current `IedBrowserPresentation` uses explicit `Section(Trigger options/Optional fields)` and `IedBrowserDetailSectionLayout.Attach` nests them dynamically. Test semantics have evolved, not simple exact-assert parity. Needs owner acceptance.
- **#583 operator GUI icons** stays OPEN: two original operator geometry keys remained exact, but **CommandDockPower** geometry changed on current RC, so visually/semantically no unconditional closure despite historic tests. Respect accepted GUI and GUI owner.
- **#594 RCB discovery values** remains OPEN: old historical test assertions no longer match new discovery/observed provenance; require source/evidence proof before closure.
- **#567/#570** phasor/measurement semantic gates not blindly closed.
- **#753/#606** independent of engine lock, MMS Write/Operate, WPF GUI redesign or accepted stable. #729 physical R7/R10 qualification separately closed pending real evidence.

## 5. Recommended D6 after this checkpoint

1. Read completed SHA `410a2fd3c8757f9c9cac46e2df6cb4ce047585b7` canonical Windows+SCL results. Repair only if red, avoid redundant rerun loops; check exact new test class in app tests.
2. Have active RC/GOOSE owner review #753 six-file diff and choose safe integration timing; protected draft and frozen physical gate MUST remain unless release process authorizes.
3. Explicitly reconcile #606 old 7-method map with current nested RCB UX and physical evidence, not obsolete string assertion.
4. With Control/Write owner, isolate native snapshot to active #615 lane and verify lost-association result suppression on a deterministic late Task; no claim of IED field success until actual FAT.
5. Continue narrow per-file+semantic old PR triage; keep original heads/branches and current stable.

**D5 real effect:** 3 evidence-based historical closures + new live code regression protection for GOOSE/SGCB (#753) + complete #606 legacy-to-current matrix + stable workstream isolation. Zero source/mainline regression asserted without CI.
