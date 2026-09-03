# FAT v2 — DataSet-driven verification implementation plan

Status: implementation branch `feat/fat-dataset-verification-v1`

## Immutable baseline

- ARSAS base `main`: `da5d4a11ab5e04686c766c8920657322a2350636`
- ARIEC61850 engine pin consumed by ARSAS: `26c85400a4da230c4429e6302847f230385b6687`
- ARIEC61850 newer `main` was audited but is not consumed by this work unless a missing engine API is proven. The newer delta is reporting/runtime work and would unnecessarily widen the regression surface.

## Product contract

FAT v2 is a DataSet-driven IEC 61850 verification workspace.

1. Every static DataSet membership becomes a FAT row by default.
2. Digital, analog, and other DataSet members are never silently dropped because of signal type.
3. Source SCL is immutable. ARSAS consumes ARIEC SCL/workspace projections and does not parse or rewrite SCL for FAT.
4. A user may explicitly remove a FAT row. Removal changes only operator disposition; source identity and evidence remain preserved.
5. Removed rows can be restored individually or in bulk.
6. FAT uses generic `Value 1` and `Value 2` evidence.
7. Discrete values use automatic transition capture.
8. Analog values use explicit operator snapshot capture from the current live IED reading.
9. Recapture may replace the current Value 1/Value 2 pointer, while the historical evidence journal remains append-only.
10. Acquisition/session state must never mutate operator inclusion/disposition.

## Explicitly deferred

The first public milestone does **not** include:

- injected/reference values;
- tolerance configuration;
- percent-error calculation;
- automatic analog pass/fail against injected values.

Those features are allowed only after the base capture workflow has public-quality evidence.

## Phase gates

### P0 — Baseline and engine authority

- [x] Establish exact ARSAS `main` SHA.
- [x] Establish exact ARIEC `main` SHA and exact ARSAS engine pin.
- [x] Compare pin to newer engine head.
- [x] Keep the proven engine pin because static DataSet authority already exists.

Gate: no engine pin movement without a concrete missing API and dedicated ARIEC regression evidence.

### P1 — Additive FAT v2 domain

- [x] Add static membership identity, signal kind, capture mode, operator disposition.
- [x] Add generic Value 1 / Value 2 evidence primitives.
- [x] Add reversible remove/restore and bulk restore domain operations.
- [x] Project exactly one row per ARIEC static DataSet membership.
- [x] Keep duplicate membership identity across different DataSets.
- [x] Add analog operator snapshot capture primitive.
- [x] Add deterministic multi-workspace aggregation and conflict policy.
- [ ] All focused IO FAT CI gates green at the current branch head.

Gate: existing FAT production files stay untouched until the additive domain compiles and regressions pass.

### P2 — Evidence/session bridge

- Replace legacy ON/OFF-only session assumptions with Value 1 / Value 2 adapters.
- Digital adapter keeps proven transition semantics while emitting generic evidence.
- Analog adapter snapshots current raw live value only after explicit operator action.
- Append every capture/recapture to the hash-chained journal before replacing current evidence pointers.
- Stop using `TestEnabled` as an internal engine/session-scope switch.
- Completed selected rows remain selected; operator Stop seals the active session.

Required regressions:

- restored completed row preserves operator inclusion;
- package open preserves operator inclusion;
- clean retest does not change operator inclusion;
- analog V1/V2 capture completes a row;
- analog recapture replaces current pointer and journals both captures;
- discrete recapture updates current evidence without losing historical proof;
- removed row receives no new evidence until restored.

### P3 — SCL source and persistence contract

- Add source kind and source artifact collection while retaining legacy workbook compatibility.
- Import one or many SCL files through `SclWorkspaceService.OpenAsync` only.
- Preserve source file SHA-256 plus IED/AccessPoint provenance.
- Use a stable combined fingerprint independent of file selection order.
- Block conflicting definitions of the same IED/AccessPoint instead of fuzzy merging.
- Allow SCL without Communication/IP to import; endpoint binding is required only before live acquisition.

Required regressions:

- one SCL imports all static DataSet members;
- multiple SCL files aggregate all unique IED/AP workspaces;
- identical duplicate source collapses safely;
- conflicting IED/AP sources block;
- fingerprint is order-independent;
- Siemens-like 36 ST + 22 MX fixture remains 58/58.

### P4 — FAT workspace UX

- Replace ON/OFF headers with Value 1 / Value 2.
- Digital rows show automatic captured values.
- Analog rows show current live value and compact capture check action for each slot.
- Right-click row: `Remove from FAT`.
- Add `Removed Signals (n)` command.
- Removed Signals window supports search, row selection, Select All, Deselect All, Restore Selected, and single-row restore.
- Remove/restore never clears evidence.

Gate: compact UI, keyboard-safe actions, no destructive SCL mutation, no hidden auto-selection changes.

### P5 — Package, report, compatibility and release proof

- Persist operator disposition and Value 1/Value 2 current evidence.
- Package all SCL source artifacts in `.arsas` projects without pretending an SCL project has a source workbook.
- Keep legacy Excel FAT projects readable.
- Export PDF/Excel reports with generic Value 1 / Value 2 and signal kind.
- Update FAT documentation.
- Run full Build ARSAS, focused IO FAT, existing SCL/DataSet regressions, package integrity tests, and release guards.

Gate: no merge to `main` until all required CI checks are green and the PR diff is audited for accidental legacy behavior changes.

## Commit policy

Use narrow commits with one invariant per slice. Prefer additive code until regression tests establish the replacement contract. Do not mix ARIEC engine evolution, unrelated reporting work, or visual refactors into FAT v2 commits. Any engine change must land in ARIEC first with its own proof, then ARSAS may deliberately update the immutable lock in a separate consumer commit.
