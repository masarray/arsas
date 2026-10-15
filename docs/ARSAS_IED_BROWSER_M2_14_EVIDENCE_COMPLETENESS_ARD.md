# ARSAS IED Browser M2.14 — Evidence-Complete Inspection ARD

Status: **HANDOFF / PLANNED**, implementation to be executed in a subsequent work thread. This document commits no M2.14 runtime change or physical acceptance.

## 0. Exact baseline and scope

- Consumer: `masarray/arsas`; current engineering candidate draft PR **#560** (not public stable), branch `feat/ied-browser-m2-model-hierarchy-report-qtime-20261014`.
- M2.13E code checkpoint: **`c8eebde9cbe83cb128410c6a16a260267929c52d`**; Windows **Build ARSAS #38646689977** succeeded on this exact SHA. Verify PR HEAD and workflow again before editing; they can change.
- `main` is a separate public/stable integration line. Never reset, rebase destructively, merge or release without explicit approval.
- `engines/ARIEC61850.lock.json` pins **`036e2c00d83a0815b6160147b14ea05975057965`** at handoff. Inspect the actual pinned engine APIs; do not silently substitute current ARIEC61850 main, vendor-specific decoding, or unverified engine changes.
- Implement native **WPF** ARSAS. Retain existing IED Explorer as IED authority (no duplicate selector).
- Follow `docs/ARSAS_IED_BROWSER_M2_EXECUTION_ARD.md`, current `docs/ARCHITECTURE.md`, `docs/architecture/INVARIANTS.md`, `docs/architecture/PERFORMANCE_BUDGET.md` and code over any stale earlier handoff plan.
- **Public repository policy:** do not name or cite competing desktop products/vendors in source, comments, tests, documentation, PRs, commits or release notes. Use “engineering reference workflow”. Keep raw vendor/private SCL captures, IPs and station files outside the public repo; sanitized fixtures only.

## 1. Field problem statement and evidence

Real BCUGE browser comparison shows:
- **GOOSE:** the browser displays missing/unchecked Enabled, GoID, DataSet, APPID, MAC/VLAN, and no last-GOOSE frame/data members while an independent engineering inspection has those details. ARSAS's existing GOOSE Subscriber may already see the stream, but its evidence is not joined to the browser GoCB page.
- **Reports:** RCB rows can show received DataSet values while RptEna/Resv/Owner and “Information received in last Report” remain missing. A live report cannot *by itself* prove `RptEna` for an unrelated RCB or reconstruct RCB control status.
- **Setting Groups:** SGCB `NumOfSG` / `ActSG` and SG/SE values are largely “Not read”. Some generated field names double-append `setVal`; flat lists lack FC-aware hierarchy/units.
- **Measurements:** `MMXU1.TotW`, `TotVAr`, `PPV`, `PhV`, `A` may show unformatted zero, unknown aggregate, or no phasor angles/units even when child magnitude/angle values are present and typed. Expected display uses properly resolved units (e.g. MW, MVAr, kV, kA) and typed phase angles only when observed, not fabricated zeros.
- **DataSets/Data Model UX:** wide 50/50 Name/Value split separates labels and values excessively; DO/DA parents lack consistent summary composition, LN order/description and nested details are weaker than engineering reference.
- Existing “Read completed: N values / N attributes” counts cannot substitute for provenance coverage by category, failed reads, unavailable controls or mapping gaps.

These are concrete code/product gaps, **not evidence that a device necessarily failed to send each missing value**. Distinguish offline SCL, saved discovery, current MMS read, current association/report ingress and passive Ethernet GOOSE capture.

## 2. Root-cause map based on inspected implementation

| Owner/entry point | Observed gap | Required change |
| --- | --- | --- |
| `Models/IedBrowserPresentation.cs` → `Details(LiveIedControlBlockModel)` | GoCB uses literal `"Not read"` for Enabled/MAC/VLAN/ndsCom; literal “No passive GOOSE reception evidence...” | Consume a typed, provenance-tagged GoCB inspection projection |
| Same file → `SGCB` | Literal `NumOfSG=Not read`, `ActSG=Not read`, `Last changed=Unknown`; SG/SE derived by naive concatenation | Bind read-only SGCB snapshot + exact model-qualified SG/SE leaves; render hierarchical names |
| Same file → `RcbExportRow` | Static “No report-reception snapshot...” despite report stream elsewhere; `RptEna` sourced only from optional RCB availability snapshot | Capture last *actual* report envelope by exact RCB identity; retain existing probe evidence |
| `Models/IedBrowserReadPlan.cs` | A Read of GoCB/SGCB/RCB resolves FC data leaves/DataSet members only; does **not** inspect control block properties | Explicit dispatch per selected inspection type; do not expand general FC Read into unchecked control services |
| `MainWindow.IedBrowser.cs` | Chooses `LiveDiscoveryModel ?? SclWorkspace?.DesignModel` | Form a provenance-preserving SCL+Discovery metadata overlay where identity verified; never blind merge |
| `MainWindow.GooseSubscriber.cs` → `_latestGooseFrames`, `BuildGooseBindingCatalog()` | Existing GOOSE stream and SCL bindings not surfaced in browser | Read-only adapter keyed to exact stream/IED identity, association/capture generation, stale-state semantics |
| `Models/GooseSubscriberModels.cs` → `GooseStreamRow` | Holds APPID/GoCbRef/GoId/DataSet/source+dest MAC/VLAN/stNum/sqNum/TTL/ordered leaves | Reuse existing model; avoid second capture pipeline or duplicated GOOSE decoder |
| `Models/RcbInspectionEvidence.cs` + `Services/RcbAvailabilityProbeService.cs` | Existing read-only RCB probe is separate from normal Read | Reuse; refine per-RCB availability/status provenance and access from selected RCB |
| `Models/IedBrowserEngineeringValuePresentation.cs` | Unit index sourced only from `SignalDefinition.Unit`; formatter handles one numeric scalar only | Model-authoritative units/multiplier plus structured engineering value composer |
| `MainWindow.IedBrowser.cs` → `RefreshIedBrowserVisibleValues()` | Per-revision unit index and scalar row rendering; no coherent phasor/3-phase aggregation | Precomputed schema references, incremental typed aggregates, stable per-row updates |
| `MainWindow.xaml` → `BrowserDataRow` | Two `ColumnDefinition Width="*"` force very wide Name/Value separation | Resizable bounded Name column + nearby Value and compact provenance, preserving virtualization |
| `Models/IedBrowserSchemaIndex.cs`, `Models/IedBrowserDetailEvidenceResolver.cs` | Existing FC-aware evidence routing and report/read precedence | Reuse and extend exact references; no new string suffix fallback identity system |

Read-only inspection endpoints in **pinned** ARIEC61850 must be audited. If GoCB/SGCB wire read support does not exist, design and test the smallest new **engine-side typed read-only** API and integrate it by explicit approved engine pin; never spoof a read result from SCL.

## 3. Nonnegotiable provenance model

Do not use one blanket `Not read` for all failures. Evidence fields must carry at minimum:
- `IED/model identity`, canonical IEC reference + FC/control-block subtype, SCL version/model revision (as applicable).
- `origin`: Source SCL / Verified Discovery / MMS Control-Block Read / Static Report / Explicit FC Read / Passive GOOSE Frame / Derived engineering aggregate.
- `association generation` or `capture session identity` and explicit observed-at time, when applicable.
- `availability`: no model, not requested, pending, read failed/denied, not supplied by device, mapping ambiguous, valid observation, stale/disconnected, unsupported service.
- For a derived value, list exact source member keys, FCs and unit metadata; no synthetic value if required children are absent.

Prefer stable internal enums/records to UI string sentinels. Adapt them to brief operator-facing text/tooltips. A source model value and actual online read may legitimately disagree: preserve both with evidence, do not silently overwrite SCL or advertise model default as live.

No alias may cross LD/DO or FC; no cross-IED joins based on `GoID` alone. RCB use/Owner markers require proven caller association or read-only device state. A report stream confirms **received data** only for members actually included in that report; no implication that entire DataSet is freshly updated.

## 4. M2.14A — source inventory and evidence binding (first mandatory code gate)

Audit exact pinned ARIEC surfaces and consumer call sites for:
1. GoCB attributes and MMS Read support; source SCL GSE address metadata.
2. SGCB services `NumOfSG`, `ActSG`, `EditSG`, `CnfEdit`, `LActTm` when supported; FC SG/SE scoped to selected setting group.
3. RCB availability and latest report envelope publication from the active report runtime (RptID, RCB reference, inclusion reason, seqNum, timeOfEntry, entryID, DataSet and GI).
4. GOOSE subscriber frame key / ordered leaf shape and strict SCL binding.
5. Measurement units from exact model DA/BDA configuration, SCL Engineering/DataType, multiplier and raw observation.

Document **field → authority → API → availability state → acceptance fixture** for every visible panel property; identify genuine engine API gaps explicitly. This is a source-based audit, not a generic rewrite proposal.

Implement one small, typed `InspectionEvidence` family + adapters per subsystem, rather than a cross-cutting god-class. Add tests proving offline vs live state, stale invalidation and conflicting SCL-vs-Discovery sources. Integrate with immutable schema index; no full-model scans on every report/GOOSE frame.

## 5. M2.14B — control-block inspection and last Report

### RCB
- A selected RCB can explicitly **Inspect** read-only state using the existing `RcbAvailabilityProbeService` (or verified engine API). Harmonize normal Read selection with this action via **typed command routing**; no unexplained disabled button or silent no-op. Preserve current exclusive/concurrent MMS constraints and clear refusal explanations.
- Show `RptEna=true/false` **only** for current valid control-block evidence, or proven local activation; `Unknown` otherwise. Similar rules for Resv, ResvTms, Owner, RptID, DatSet, TrgOps, OptFlds, BufTm, ConfRev, IntgPd.
- The three sections `Control Block attributes`, `Information received in last Report`, `Data` must independently expand/collapse; preserve state on value refresh.
- Cache and project **last actually received Report envelope** per exact RCB and session: timeOfEntry, inclusion reason, sequence number, entryID (only if present), updated member count and provenance. The report frame may be identified by RCB Ref/RptID only with a checked unique mapping; shared DataSet alone is insufficient.
- Do not infer report reception from static model, the number of reports or RCB enable state. Disconnect marks snapshot stale. Never generate GI, write, reserve, enable or toggle RCB just to display inspector values.
- BRCB/URCB and ownership badge behavior from M2.13 remains intact.

### SGCB
- Read SGCB control state **read-only** from authoritative service when supported; `NumOfSG` / `ActSG` / last change display must be real, timestamp semantics explicit.
- SG/SE entry mapping must be exact path + FC + effective setting group. Avoid duplicate paths `setVal.setVal`, never flatten away intermediate structured values, and display units/types correctly.
- Do not perform `SelectEditSG`, `ConfirmEditSG`, change active group, write or Operate in inspection workflow. If device denies SG read or the engine lacks API, state `Not supported` or `Read denied` with reason instead of fake values.
- Add regression fixtures for multiple SG groups, one unavailable group, unambiguous FC SG/SE, and no writes.

## 6. M2.14C — GOOSE engineering browser integration

**Three distinct authorities**:
1. Source SCL `GSEControl` / `Communication`: GoCBRef, GoID, DatSet, ConfRev, APPID, destination MAC, VLAN ID, VLAN priority, min/max times **where actually declared**.
2. Read-only MMS GoCB attributes: GoEna, GoID, DatSet, ConfRev, NdsCom when supported. A GOOSE frame by itself cannot prove the GoEna control attribute is presently true.
3. Existing passive Ethernet Subscriber: frame source MAC, `simulation/test`, last-seen time, stNum, sqNum, timeAllowedToLive, TTL remaining (computed only from valid observed time), ordered DataSet entries and q/t from captured frame.

Use `MainWindow.GooseSubscriber.cs` `_latestGooseFrames` / `BuildGooseBindingCatalog()`, `GooseStreamRow`, `GooseSubscriberRuntime` and engine GOOSE codec; **no second sniffer, frame decoder, or capture device ownership**.

Match selected IED GoCB to captured stream with **verified GoCbRef and model association**, then DataSet reference, confRev, APPID/MAC where available; ambiguous or contradictory evidence must be rejected and explained. Preserve association/capture epochs, lock-free read-only snapshots or bounded reader handoff to WPF, and no per-frame full-model rebuild.

GOOSE Browser `Control Block attributes`, `Information received in last GOOSE` (or sent only if the UI is explicitly in a local publisher context) and `Data` are individually collapsible. Data shows real frame leaves only after matching capture; static modeled membership remains visible offline but not labeled as received values. Explicit Start/Subscribe is a user action in the existing GOOSE tab; selecting the Browser node does not turn capture on.

Fixtures must cover captured frame with four ordered entries and q, valid SCL link, missing SCL GSE address, wrong APPID, duplicate GoID across IEDs, stale frame after restart, VLAN-unavailable capture and capture-not-running.

## 7. M2.14D — schema-backed measurement composer

Design one reusable typed value composer (engine formatter where appropriate, consumer display adapter only):
- FC-aware `mag.f`, `instMag.f`, `cVal.mag.f`, `cVal.ang.f`, `q`, `t`, `units.SIUnit`, multiplier and configured scaling; preserve origin and timestamp/quality.
- Scalar unit formatting e.g. `0 MW`, `0 MVAr`, `0 Hz` only when model/engineering metadata proves the unit; do not hardcode units from `MMXU`, signal name or a screenshot.
- Polar complex value `X unit ∠ θ°` only if both magnitude and angle are observed, compatible and typed; otherwise use a partial/unavailable indication without creating angle zero.
- Phase vector `phsA, phsB, phsC` and paired phase-line `PPV` semantics from *schema order and members*, not index guesses; no cross-phase fallback. Compose a concise parent DO display while maintaining expandable children.
- Config FC `CF` rows such as units, db, dbAng and `DC` descriptions remain browsable and unit display is accurate; do not treat them as live process values merely because a model defines them.
- Indexed, revision-aware aggregation with bounded work per changed leaf and no full model scan per report. The same formatter feeds Browser/Explorer/Live Monitor when referring to the same typed evidence.
- Golden fixtures include three-phase all zero with real observed angle, missing phase B, missing angle, mismatched timestamps, unknown units, multiplier scaling and conflicting FC. Verify no guessed value and correct fallback.

## 8. M2.14E — compact, familiar native WPF GUI

Do not replace the working virtualized Browser. Amend `MainWindow.xaml` and shared `BrowserDataRow` layout:
- Name/Value immediately adjacent; set fixed-resizable Name to approximately 320–420 device-independent pixels at regular desktop width, or responsive bound; Value wraps/truncates sensibly; reserve Source/provenance narrow and secondary. Do NOT use `*` + `*` on a very wide pane as the only layout.
- Keep compact alternating rows, minimal text weight, clear selected row, keyboard/mouse expansion, scroll stability, lazy child materialization, small chevrons, adaptive width and high DPI.
- DataSet panel is hierarchical DO/DA with inline expansion; Data Model shows LD → LN listing with concise LN description, then DO/DA/BDA + q/t + CF metadata and composed engineering summary.
- GoCB, RCB and SGCB property subsections independently collapse/expand; right header clearly marks source Model/Live/Received/Offline. No noisy status blocks or bulky cards.
- Left Browser tree remains shallow to LN; outer IED Explorer owns selection. Preserve minimal hover splitter, Quick Start Browse IED, Files three-column auto-download/native viewer and existing navigation/Back behavior.
- UI changes must not require rebuilding all rows on each live update; test 1000s of model nodes and repeated report bursts.

## 9. M2.14F — physical and regression acceptance

**BCUGE** and **ZIV AVR** acceptance, with operator-supplied sanitized evidence and captured frames; exact IP/site material not committed:
1. Select GoCB corresponding to BCUGE control `LLN0.BCU`. Verify GoCB metadata based on SCL/MMS; start existing subscriber explicitly; verify last frame `stNum`, `sqNum`, TTL, timestamp and four ordered DataSet values. Capture stopped must not show current reception. No fabricated GoEna.
2. RCB selected, real device probe returns correct `RptEna`, Owner/reservation or typed unavailable; last Report section updates only when exact matching report arrives; unrelated report cannot overwrite it.
3. SGCB BCUGE shows group count/active group when actually read, SG/SE entries and coherent names/units. No SG write or setting change.
4. ZIV/BCUGE `MMXU` measurements and phasors match actual observed scalars, units, phase order, angle and q/t; `Not read` only where genuinely unavailable and read diagnostics explain why.
5. DataSet and Data Model share equivalent values/provenance for the same exact FC leaf, parent summaries only if valid children exist; no cross-IED contamination.
6. Read selected, Read All, cancellation, disconnected, busy and second-association-refusal paths do not disturb active static-report-only monitoring.
7. GUI side-by-side acceptance: Name/Value proximity, hierarchical inline expansion, LN summaries and collapse groups; scroll/selection preserved during live updates.
8. **Unchanged** static reporting baseline and GI semantics, pinned-engine lock, SCL export, Open SCL / Discovery parity, Files auto-open, and Event Log chronology.

Required targeted tests: existing `IedBrowserPresentationTests.cs`, `M2IedBrowserHierarchyAndReportWireTests.cs`, `M2IedBrowserTimeAndReferenceTests.cs`, `GooseCanonicalEngineeringTests.cs`, `GooseWireDataSetInspectorRegressionTests.cs`, `GooseIedScopeAndQualityTests.cs`, `Iec61850ValueFormatterTests.cs`, `LivePowerMeasurementRegressionTests.cs`, `StaticWholeFcdPhaseMeasurementTests.cs`, `RcbExportEvidenceAcquisitionRegressionTests.cs`, `DeterministicStaticReportPathRegressionTests.cs`, `StaticDataSetReportOnlyModeRegressionTests.cs`, plus new focused control-block, join, unit, stale and UI tests.

Tests shall include verified examples, negative/error cases and no-side-effect invariants, not only string-presence assertions. Device acceptance is distinct from unit tests and green CI.

## 10. Implementation and CI discipline

- **Before each phase:** inspect latest PR HEAD, changed files, pinned-engine API, ARD/invariants and current code. Preserve M2.13E checkpoint; use draft branch/small incremental commits, no destructive reset.
- Sequence: **A evidence map → B read-only RCB/SGCB → C GoCB/subscriber projection → D measurements → E GUI → F field acceptance**. Within phase, write tests for first proven failure, then minimal implementation, then targeted test/build.
- Do not run dozens of heavy GitHub workflows per micro-commit. Prefer one coherent commit/checkpoint and targeted local/isolated tests; use full Windows workflow once implementation is ready for its phase gate. Inspect repo workflow routing before push; do not disable protections merely to save CI.
- Provide exact SHA, changed files, test counts, source-of-truth/authority rationale, demonstrated UI behavior and remaining physical gaps with every milestone. Windows portable artifact must match the reported SHA. Preserve draft until physical acceptance and explicit merge approval.
- If a necessary engine read capability is missing, stop that narrow feature with documented boundary, create an independently tested engine PR, request explicit pin update, then continue; **never** silently write proprietary MMS services in consumer or substitute unverified source values.
- On ambiguity: **fail closed**. A visible `Unknown (not checked)` with actionable Read is better than an invented success state. No automatic GI, RCB writes, SG edits, dynamic DataSet mutation or cyclic MMS polling in the static report-only browser.
