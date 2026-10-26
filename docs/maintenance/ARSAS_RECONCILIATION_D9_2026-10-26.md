# ARSAS — D9 reconciliation checkpoint: exact GOOSE FCDA safety and owner-fenced historical audit

**2026-10-26, Asia/Jakarta.** Continuation of [D8](ARSAS_NEXT_THREAD_HANDOFF_D8_2026-10-26.md), [D7](ARSAS_RECONCILIATION_D7_2026-10-26.md), [D6](ARSAS_RECONCILIATION_D6_2026-10-26.md) and D1–D5. This checkpoint lives only on isolated `audit/arsas-reconciliation-d9-20261026`, branched from D8 checkpoint `64e8baac08e86d2197b6728be0d85f70a0491840`. No main, release or RC mutation. Historical initial inventory and all previous D1–D8 decisions remain authoritative; **do not redo or reverse those closures**.

## A. Live state, before/after D9

GitHub REST full pagination re-fetched (open pulls: 100 + 16, open issues: 100 + 33, filtering issue entries with `pull_request`): **116 open PR / 17 independent open issues** before, **116/17** after. **Zero historical PRs closed** in D9: #569 and #570 contain semantic differences which preclude safe administrative closure. D1–D7 total remains 39 original historical PRs closed unmerged (with one new draft #753 offset) and two independent issues previously closed with code-only truth.

Frozen refs checked after actual source write:
- Consumer main `14e5dddfbaae012b61a021366c1a864d83d316f2`.
- Engine main `3d21238d0b62f24faecd5948a4ee238c82b35966`.
- Integrated RC #727/#729 head `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`.
- Trial RC engine immutable lock `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`, **not** engine main.
- Accepted public preview/stable milestone **v1.6.42 untouched**. Published release is not physical R7/R10 qualification.
- Active owner PR heads checked: #726 `42c7cfe1364faaeeee64180bd82e5dbef4f7355a`, #716 `f53857d5f66d55d80b0f4d04eec91df5fc48053d`, #688 `774321313aac96ec87a4f24edf61731d032237e6`, #732 `c47a622037110a09ed1e16653aac0c44b5f40de2`; none changed in this reconciliation batch.

## B. Real source/test milestone on existing draft #753 (not merged)

**NEW exact head:** [`0652f66bed0c72dded20ff547e23e2ef1989341c`](https://github.com/masarray/arsas/commit/0652f66bed0c72dded20ff547e23e2ef1989341c). Parent `410a2fd3c8757f9c9cac46e2df6cb4ce047585b7` was fully green on its own exact CI, but **that old result cannot attest the new head**. Existing PR [#753](https://github.com/masarray/arsas/pull/753) remains OPEN/DRAFT/mergeable, **6 changed files** vs RC, ahead 7/behind 0; no new PR was opened.

**Two current-tree files changed in one lease-checked Git commit, not copied from divergent historic PRs:**
1. `Models/IedBrowserGooseInspection.cs` old branch blob `f97f6bcbee2050bf6744919572ee69133fefa85d` → new `d3fe77ffa3d00a3d09d94e77cd4aa305d0acf0ad`. `VerifiedMembers` now requires exact received `frame.DataSetReference` equal to `directory.Reference` in existing canonical IEC reference normalization, checks every declared zero-based directory index, every received zero-based `DataSetIndex`, and one-based `Order`. A different cached DataSet or noncontiguous member indices must fail closed even where count and apparent member references happen to match. Previous exact reference/FC/value checks remain.
2. `tests/ARSAS.Tests/IedBrowserGooseInspectionTests.cs` old branch blob `655919201fac5ac5428db11c0a7328f15066d73c` → new `f734c2590dc583d44e9bea9e3a38a1bcbdb2ef82`. Deterministic cases for mismatched wire DataSet, bad `Order`, noncontiguous directory index; 129 wire entries with corrupt **last** slot must reject the *entire* unbound PDU even though UI displays at most 128 rows. Positive full 129-slot case included. No IED operation, fabricated quality/time/FCDA, duplicated GOOSE subscriber or new capture worker.

**Fresh exact-head CI as observed after the commit** (not yet complete at checkpoint):
- [Build ARSAS run 40308062610](https://github.com/masarray/arsas/actions/runs/40308062610) = `in_progress`, head `0652f66...`.
- [SCL Interoperability R7 Build run 40308062583](https://github.com/masarray/arsas/actions/runs/40308062583) = `in_progress`, same head.
- [P1 advisory run 40308062656](https://github.com/masarray/arsas/actions/runs/40308062656) = `completed/success`, same head.
- Prior exact parent Windows [40299449108](https://github.com/masarray/arsas/actions/runs/40299449108) / SCL [40299449103](https://github.com/masarray/arsas/actions/runs/40299449103) were fully successful and produced a prior-head portable artifact, **not** a validated artifact for D9's new head.

[Explicit new-sha safety + CI comment on #753](https://github.com/masarray/arsas/pull/753#issuecomment-6413912697). **NO promotion to stable, no merging #753 until #606 owner/RC integration review and required field gates.**

## C. Source-backed historic audits; two deliberate KEEP OPEN dispositions

These are **not** ancestor-only conclusions. Specific original PR files, current RC blobs, historical regression/test methods and changed assertions were compared. Both old branches remain intact.

### #569 — SGCB setting value presentation and SG-only read

[Audit comment](https://github.com/masarray/arsas/pull/569#issuecomment-6413914214). Original head `020e1b0272460fc0b1e9d28e40a9139f6df6a84e`, ancestor RC ahead 1395/behind 0. Four original paths compared:

- `Models/IedBrowserPresentation.cs`: `c031b2f36b8903df6c796eea3601f3bf1a10cb77` → RC `1b40ab6d53f7056944be507cefebc156054c2207`.
- `Models/IedBrowserReadPlan.cs`: `59f267fa74cdfa5a22f388aacb8d95d7bb991785` → `69bb7157e41b325f2de55a2d5e0eea75ef683bfb`.
- `tests/ARSAS.Tests/IedBrowserActiveSettingProjectionTests.cs`: `2532b043f17188ed0c3a2425593730219aab469c` → `fb50ef57422537345fcb1f42b0df65da3c77b7bb`. Four test methods retained; edit-only expectation **changed**.
- `tests/ARSAS.Tests/IedBrowserPresentationTests.cs`: `9a8f602cf62ebaf949f5669a7e8525a1fdd90ecf` → `204c746e2d1ffd9d1975b2099dcb2f7919218e9c`. 27 total test attributes remain, but unrelated COMTRADE/navigation test naming/UX also evolved.

**Disposition: KEEP OPEN. Canonical carrier: RC. Unique delta:** historic edit-only model expected `No active-group [SG]`; RC has `No model-proven SG or SCL SE` and presentation supports SCL SE static fallback. Distinguish active live SG, static SCL SE, actual MMS Read authority; do not claim equivalence solely from 4 same test names. **Safety:** no EditSG/CnfEdit/Operate authorized. **Follow-up:** owner accepts UX/read-only static provenance, confirms SG test cases, then decide closure.

### #570 — scalar MV versus phasor angle

[Audit comment](https://github.com/masarray/arsas/pull/570#issuecomment-6413914989). Original head `46adacd24ff56d70381ab1b386d9daae82f0bc1b`, ahead 1394/behind 0. All three paths compared:

- `Models/IedBrowserEngineeringComposite.cs`: `f7ff62dd317373526ad07de4cb402c7d0cb9a265` → RC `90c19fe2ff52c2c0bda6ae2379165bba20acd792`.
- `Models/IedBrowserStructuredDetails.cs`: `2e15274b69f8912527010c15a66a02a138e82582` → RC `c86778db18d5c2db3769dc4af3b7895e2096ee02`.
- `tests/ARSAS.Tests/IedBrowserEngineeringCompositeTests.cs`: `4a73bb3854451cb961f8315f52a673eeac1e8842` → RC `a7ccd02e7cdec147c87e6238f161bde14c3f379d`. All original ten test methods retained, six bodies byte-identical including scalar MV vendor-angle exclusions; total current = eleven.

**Disposition: KEEP OPEN. Canonical carrier: RC. Unique delta:** historic conflicting timestamp expected `12 (angle not verified)` and suppressed angle; RC allows `12 ∠ 33°` with provenance explicitly labeling separate/unpaired observations. Missing-angle label also changed. There is no proof that the evolved UX is field-accepted as equivalent to original fail-closed display policy. **Safety:** no invented zero angle, but no assumption of correlated timestamps. **Follow-up:** measurement/phasor owner reviews time correlation, provenance and physical device displays, also coordinates sibling #567, before possible closure.

Historical #599/#594/#583/#595/#582/#567, #606 and #615 remain OPEN for already documented meaningful blockers; no mass closure.

## D. Cross-thread command/Write/RC safety coordination

[Cross-lane execution gate on #727](https://github.com/masarray/arsas/pull/727#issuecomment-6413913508). Exact RC's alias `MainWindow.xaml.cs → Iec61850MonitorRuntime.ExecuteControlAsync → NativeIec61850Client` remains **source-proven**, as documented on [#615](https://github.com/masarray/arsas/pull/615#issuecomment-6410774907). `SignalDefinition` WPF model can be mutated by a late native command on a retired association; this is NOT physical misoperate proof. **Do not close #615**.

Acceptance owner contract: create detached **command-only** request snapshot before native dispatch; retain only currently verified ctlModel/CDC/target authority, no promotion of process q/t/Report; use nonreusable association token for UI result publish; suppression of retired native completion; deterministic TaskCompletionSource timeout/reconnect/late-mutation, second-command rejected and UNKNOWN quarantine tests. Preserve original wire SBO/Operate single-flight/quarantine and bounded teardown. User-accepted #726 Write/readback is frozen and #716 Boolean Write is separate; never overwrite either or SG7/SMV/GUI. Review by existing Command integration owner, not another competing broad WPF patch.

Additional owners: #688 SG7 performance, #732 IP/UI, #727/#729 RC/physical FAT, #606 seven-case GOOSE/SGCB, #615 control, #753 corrective read-only. Their changed file sets/branches must be rechecked immediately before merge.

## E. Handoff to D10: prioritized, no backwards progress

1. Check **exact `0652f66...`** Windows and SCL Actions job conclusions + test logs; if failing repair only substantive defect; avoid redundant CI reruns. Collect portable EXE only from new exact head after full CI success. PR #753 remains draft until reviewed #606 owner parity/integration and physical evidence.
2. Request current RC owner assessment of #753 DataSet/ordinal stricter admission (especially canonical DataSet name from physical devices). Owner decide isolated RC merge only with exact approved baseline and new CI.
3. Prioritize #615 alias repair in isolated Command-specific integration lane, preserving frozen Write, accepted SMV and native UNKNOWN/single-flight semantics. No blind historic cherry-pick.
4. Audit #569 SG/SE, #570/#567 scalar/phasor, #594 RCB provenance and #599 GUI under actual owner/field policy, not arbitrary PR count target. Later #595/#582 require complete multi-file parity before dispositions.
5. Complete GitHub PR/issue pagination again when reporting D10 counts; the 116/17 here is an observed D9 snapshot, not a guaranteed future count.

**D9 deliverable:** one real source+test safety commit on active draft #753, four actionable PR evidence/coordination comments (#753/#727/#569/#570), two completed exact-source historical audits resulting in *safe hold* (zero closures), an isolated handoff checkpoint, and zero stable/main/engine pin mutation. Automated CI still running at checkpoint; R7/R10 physical release safety remains blocked.
