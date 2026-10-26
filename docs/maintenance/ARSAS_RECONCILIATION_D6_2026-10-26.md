# ARSAS Reconciliation D6 — exact-head CI closed + isolated schema and failure-diagnostic legacy carriers reconciled

**Date:** 2026-10-26 Asia/Jakarta. **Read first:** [D5](ARSAS_RECONCILIATION_D5_2026-10-26.md) → [D4](ARSAS_RECONCILIATION_D4_2026-10-26.md) → [D3](ARSAS_RECONCILIATION_D3_2026-10-26.md). This checkpoint lives on the **independent audit/maintenance branch**, NOT on main, integration RC, engine or accepted stable. No historical branch deleted. Original reconciliation inventory is immutable on its earlier audit ref.

## 1. Fully refreshed live GitHub state

| Measure | D6 start | D6 verified exit |
| --- | ---: | ---: |
| Open pull requests, paginated REST all pages | 122 | **120** |
| Open independent issues, PRs excluded, both pages | 17 | **17** |
| D6 administrative closures | 0 | **2 closed, UNMERGED** |
| New D6 PR | 0 | 0 |
| New D6 code/Windows workflow changes | 0 | 0 |
| Draft #753 6-file P0 current-tree patch | CI in progress at D5 | **EXACT NEW HEAD canonical Windows + SCL succeeded** |

**Original open snapshot 154 PR/19 issues → current 120 PR/17 issues.** Totals: **35 historic PRs closed safely across reconciliation**, plus **two issues already closed in earlier batches**, offset by the one *new* distinct safety corrective draft #753 (one reason 154 - 35 + 1 = 120). Counts are live snapshots, not independent proof of feature completeness. This D6 batch did not close any issue.

Protected refs cross-checked live and unchanged:
- Consumer `main`: `14e5dddfbaae012b61a021366c1a864d83d316f2`.
- Engine `ARIEC61850/main`: `3d21238d0b62f24faecd5948a4ee238c82b35966`.
- Integrated draft RC source for [#727](https://github.com/masarray/arsas/pull/727)/[#729](https://github.com/masarray/arsas/pull/729): `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`.
- Published accepted stable `v1.6.42` remains untouched and unretagged; **the RC trial engine lock is not engine main and must not be casually repinned**.
- All protected active #753/#606/#615/#727/#729/#726/#716/#688/#732/#374/#435/#546/#649/#543 remain OPEN. Reconciliation did not edit their code except owning targeted #753's earlier D5 test commit; #753 D6 head did not change.

## 2. D6 closure #597 — full 4-path schema authority retained

[Historic PR #597](https://github.com/masarray/arsas/pull/597) original HEAD `326525c90cec384190abf21eda43510153dfc17d`: strict ancestor of protected RC (+1275/-0, merge-base historic head). [**Individual 5-field closure evidence**](https://github.com/masarray/arsas/pull/597#issuecomment-6412973161). Closed **unmerged**; source/branch preserved.

- **Identical production blob:** `Models/IedBrowserIsolatedReadSchemaLease.cs` `8ffa03bf40ba7f29db9ebbdc04aeea2c8082dd2b`. Live monitoring association/model reference/IP/port/Browser projection epoch and offline fence, never SCL-only authority.
- **Identical all-four-test blob:** `tests/ARSAS.Tests/M214IsolatedBrowserSchemaLeaseTests.cs` `26842618182ef5372eef6eea14eb21f981242d09`: schema lease success, SCL/offline refusal, endpoint/model/association invalidation, native client refusing disconnected injection.
- Evolved `Services/IedBrowserIsolatedReadService.cs` old `3b1521c8abcc5ca1d0b6f55f2996964c9ecc9ef1` → RC `9d0ad89e99ba1953c354faace8c6f13b529b712b`: captures lease before Task.Run; checks current device before binding & before reads; invokes the exact `BindIsolatedBrowserReadSchema(lease.Model)`. New typed-root fallback additionally requires model/verified wire kind, not a broad unsafe parser.
- Evolved `Services/NativeIec61850Client.cs` old `0cff7ebaa10764e025f9b488bae1356e7922f112` → RC `b74398faf4d206286ab1dcac8b6e5ab414783393`: identical fresh **connected secondary client** guards and reference-only schema binding; no writer or double discovery.

This documents equivalence to the unique historical *schema-binding* requirement. It is NOT a claim a particular relay allows a second MMS client or that a physical test has succeeded.

## 3. D6 closure #598 — 3-path observed failure truth extended, not lost

[Historic PR #598](https://github.com/masarray/arsas/pull/598), original head `3b87e653cd73a312d6662941135999e1cd0a1982` exact ancestor RC (+1272/-0). [**Individual 5-field closure evidence**](https://github.com/masarray/arsas/pull/598#issuecomment-6412975445). Closed **unmerged**; original source/branch intact.

- `Models/IedBrowserReadFailureEvidence.cs` original `4b315bb3e61ffa76bac27e4f156be8ee10d83ec0` → RC `391f5c0c2a4b0673472eb4e9768277dc7548b7e3`. Retains **4,096** bounded absence-only entries, **320-character** individual reason bound, exact canonical reference+FC, same owner association and model/epoch fence, no fallback guess, no scalar storage. New `IedBrowserReadAbsenceKind` distinguishes **Failed / Interrupted / Deferred**, lock guards concurrent access, and prevents a budget-deferred marker from erasing actual wire-failure evidence.
- `MainWindow.IedBrowser.cs` original `6600753185f5761c3fe90fa0e9e91c0fc9a37c46` → RC `21a0551413a5437a562c463f011204868aab7b3e`: still records exact Read failures, clears on successful observed/read-ledger publication, shows **Unavailable/MMS Read failed** for failed target, but distinguishes **Not read / Initial read deferred** for unattempted budget cases and **MMS Read interrupted** for uncertain flight. Same association/current owner enforced. No fake successful value/quality/RCB promotion.
- `tests/ARSAS.Tests/M214BrowserReadFailureEvidenceTests.cs` old `e55171d714c956166544408dc96270a27ecca4a5` → RC `eab72838b0107a0a54e86521f865b9161e6159e2`. All **four original test methods AND original assertion lines** retained: exact SG FC, no sibling bleed, fallback declared only, success clears, reconnect/catalog/owner invalidation, missing/oversize bounds. New regressions explicitly cover deferred budget and interrupted uncertainty.

Neither historical closure touched safety critical engine/Control/Write/GOOSE code. No physical IED test acceptance implied.

## 4. Real release/CI milestone at existing P0 draft #753 — exact 6-file head qualified by automated checks

[Safety PR #753](https://github.com/masarray/arsas/pull/753), exact head `410a2fd3c8757f9c9cac46e2df6cb4ce047585b7`; targets RC `817c3910e8223fa2ac531a4426ada5d2d4bbbe50` (6 files, 3 source/3 test). GitHub reports mergeable clean, still **OPEN DRAFT, UNMERGED**. The P0 code guards full raw unbound GOOSE PDU completeness/unique order and rejects foreign-domain SGCB/GoCB read-only model inventory, with synthetic and current-tree #606 continuity regressions.

[Exact-head Windows Actions run 40299449108, job 121493442949](https://github.com/masarray/arsas/actions/runs/40299449108/job/121493442949) **completed SUCCESS**. All **36 job steps inspected; zero failures**. Explicit success steps: immutable ARIEC engine/ArdIrec native bridge lock, .NET8 solution build, pinned engine and ARSAS application regression, exact-SHA test attestation, pinned native **CTest**, packaged native managed integration, portable single-EXE publish, portable EXE smoke, sealed Windows input. [Exact-head SCL run 40299449103](https://github.com/masarray/arsas/actions/runs/40299449103) **SUCCESS**. Changed-area classifier success; reusable build comparison skipped by design, not failure.

Artifacts **from this six-file SHA** (GitHub Actions build outputs, not published accepted stable):
- [Application regression evidence #12640350496](https://github.com/masarray/arsas/actions/runs/40299449108/artifacts/12640350496), artifact sha256 `c512379e5758d0fba9bcb1fd939d2da474e4c4bdc9bc9ca56f5cec9c702fa266`.
- [Native CTest evidence #12639722807](https://github.com/masarray/arsas/actions/runs/40299449108/artifacts/12639722807), sha256 `089c991e69909609a96e4304e5967661d82504310cfdb80b90da4b84d8ba9778`.
- [Portable Windows single EXE #12639857894](https://github.com/masarray/arsas/actions/runs/40299449108/artifacts/12639857894), artifact sha256 `405f21003fce3394cb454c1f97b39df61d46a2f71001047364d0973271313f15`.
- [Sealed Windows package input #12639887804](https://github.com/masarray/arsas/actions/runs/40299449108/artifacts/12639887804), artifact sha256 `ce37084a4471324b3e2e1412b4bca636f77047c6dc0010eb584d8e89405a8674`.

[New immutable six-file CI + decision comment on #753](https://github.com/masarray/arsas/pull/753#issuecomment-6413020668). No CI rerun requested. **Do not promote this artifact to stable**, mark released, or infer GOOSE PDU physical publication/SGCB interoperability/physical R7/R10 qualification from CI.

## 5. Why several tempting historical closures were deferred intentionally

- **[#599](https://github.com/masarray/arsas/pull/599)** original test expected last-Report section **expanded by default**, current RC `IedBrowserDetailSectionLayout.Attach` intentionally collapses long Report/GOOSE summary by default; original raw-view RCB Trigger/Optional flag sections became visually nested under Control Block via layout adapter. Original test/assertion set is **not identical**; require accepted UI/RCB owner review, not administrative closure.
- **[#594](https://github.com/masarray/arsas/pull/594)** old discovery-state RptEna/Resv/ResvTms projected "historical"; current RC intentionally changes dynamic vs static provenance/values and old test methods/asserts differ. No closure until proof against received MMS and owner acceptance.
- **[#583](https://github.com/masarray/arsas/pull/583)** command-dock user-supplied icon geometry was changed on RC; cannot assert original exact glyph parity even though Explorer/Diagnostics/Gose remain. Active GUI owner decides.
- **[#584](https://github.com/masarray/arsas/pull/584)** has identical 100-line historic selected-DO regression file but changes six WPF/Read files; current UI/deferred click navigation and selected-DO handling need targeted code/UX audit, not blindly close.
- **[#592/#589](https://github.com/masarray/arsas/pull/592)** GUI/SMV passive routing and events touch actively owned tabs. Preserve until current selected-tab/live capture behavior and tests fully checked.
- **[#567/#570](https://github.com/masarray/arsas/pull/567)** measurement/vector phasor mapping still independent semantic blockers; avoid closure by ancestral status alone.

## 6. Protected owner handoffs and next batch

- **#606:** historical seven-test case matrix [documented with real current code](https://github.com/masarray/arsas/pull/606#issuecomment-6412633849); #753 six-file exact-head CI now green; still needs reviewed **RC integration** and intentional RCB visual/GOOSE subscription parity acceptance. Do not close #606 merely because tests passed on draft branch.
- **#615 P0:** previous [source-proven Control GUI/native mutable-alias handoff](https://github.com/masarray/arsas/pull/615#issuecomment-6410774907) still unresolved; design snapshot-before-native, strict nonreusable association-fenced feedback, old timed-out task and quarantine tests in existing #726/#716 Control/Write lane. **No competing code branch or premature Operate claims created here.**
- **#729/#727:** release trial engine lock, R7/R10 physical signature and acceptance remain independently gated. Keep stable `v1.6.42` immutable until authorized release process.
- Next engineering action: owner-review #753 against RC and typed #606 seven-test matrix, decide merge only at approved RC merge gate; secure exact frozen SHA and re-run canonical CI if head moves; physical GOOSE/SGCB/capture proof separately. Evaluate #594/#599 visual/data provenance against accepted current GUI, continue bounded historic #595 cohort with all changed source/tests and no owner conflict.

**Bottom line D6:** two more sound historical closures + exact-head comprehensive CI on real current-tree GOOSE/SGCB source/test remediation + immutable artifacts/provenance. No unsafe merge, no issue mass closure and no fabricated field FAT.
