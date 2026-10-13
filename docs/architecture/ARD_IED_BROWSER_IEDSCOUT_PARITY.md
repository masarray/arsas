# ARD — ARSAS IED Browser: IEDScout Global Data / Browser Workflow Parity

**Document ID:** ARD-ARSAS-IEDB-001  
**Version / date:** v1.0 — 2026-10-13  
**Status:** PROPOSED / DESIGN ONLY (no GUI or engine change authorized by this document)  
**Owner:** ARSAS consumer; ARIEC61850 protocol engine  
**Reference:** user-supplied IEDScout interaction recording `Arssyut-20261013-152821-pixel-accurate.mp4`, ARSAS screenshot `image(20261013-091856).png`, OMICRON IEDScout 4.20 guidance.  
**Language:** Bahasa Indonesia for product decisions; IEC 61850 terms retain normative names.

## 0. Decision record / audit provenance

- Audited **ARSAS main** tree `7d8d8026ae96d45f44d47f361d115a437af88fbe` (no Browser implementation in that tree).
- Audited **ARSAS** branch `feat/p10-ied-browser-deferred`: `MainWindow.IedBrowser.cs` and Browser markup inside `MainWindow.xaml` exist. This branch is a **reuse candidate, not a claim that its source matches the richer operator screenshot**.
- Audited **ARIEC61850 main** tree `e5deed1d8aa11d97991695c6e390baafea7ab797`, `LiveIedModelDiscoveryModels.cs`, `MmsDataSetDirectory.cs`, `MmsReportInventory.cs`, `apps/AR.Iec61850.IedDiscovery/ViewModels/DetailRows.cs`.
- Mandatory **pre-implementation gate**: establish the exact branch/commit/build that produced the operator screenshot; compare it with `main` and `feat/p10-ied-browser-deferred` before editing. Do not reset the more mature build to the deferred code. Audit existing native UI separately from screenshot, then choose the latest verified base.
- Architecture invariants: `docs/architecture/INVARIANTS.md`, `docs/architecture/PERFORMANCE_BUDGET.md`, `docs/ARCHITECTURE.md`.
- Implementation restriction: **this ARD is a documentation-only specification**, not authority to modify MMS, RCB activation, discovery, control, or reporting.

### Baseline deficiencies observed

1. Deferred implementation fragments interaction across four nested sub-tabs: RCBs, DataSets, IED Model, Signal Catalog. The target is one consistent left TreeGrid + right DetailGrid, with object-specific detail templates.
2. Deferred implementation uses imperative WPF `TreeViewItem` creation plus fixed `IedBrowserWorkspaceIndex = 7`; both conflict with resilient MVVM/selection/navigation.
3. `RefreshIedBrowser()` clears multiple `ItemsSource` values and tree nodes; target shall preserve selected node, expansion, scroll position and active detail where the same model identity still exists.
4. Existing RCB check is a separate explicit read-only availability check, not a general-purpose per-object live detail read. Do not label it as full RCB / DataSet / SGCB live reading.
5. Model has SGCB / GOOSE inventory, but presence of a model object **does not prove** that every field is readable via the current MMS API.
6. The screenshot establishes expected information density and hierarchy; its dark navigator and grey detail rows are **behavioral references**, not colors to duplicate blindly.

## 1. Product objective & constraints

**Goal:** An IEDScout-experienced protection/substation engineer must be able to inspect an IED without relearning where Reports, DataSets, Setting Groups, Files, GOOSE, and Data Model live, how child objects expand, and how to invoke a deliberate read.

### In scope

- Single Browser workspace with exactly **two persistent in-workspace panels**: left virtual TreeGrid and right virtual DetailGrid; the application's preexisting global IED Explorer is outside this two-panel definition.
- Browse offline SCL or stored discovery model with the **same navigation and detail templates** used for live IEDs.
- Explicit, bounded read-only operations for supported objects, inline status/errors, precise provenance and last-read times.
- Relationship navigation: RCB ↔ DataSet ↔ Data Model; SGCB ↔ setting attributes; GoCB ↔ DataSet; Files ↔ download/view (explicit only when supported).
- Keyboard interaction, context menus, large-model performance, multi-IED state and regression gates.

### Out of scope in the first release

- No rewrite of MMS/ACSE/BER or protocol service sequencing in ARSAS.
- No silent auto-discovery, auto-reconnect, RCB enable/disable, reservation, GI, GOOSE publisher enable, control operate, setting-group switching, SG/SE write, or file transfer on selection.
- No change in static DataSet **report-only** acquisition authority or fallback policy.
- No invented unknown live values, missing report fields, SGCB timestamps or GOOSE addressing.
- No pixel copying or third-party control/code extraction from IEDScout.

## 2. UX shell decision (DEC-01)

```text
┌─ ARSAS main application ───────────────────────────────────────────────────────┐
│ Explorer | Live | Events | Alarm | GOOSE | Diagnostics | FAT | [IED Browser]   │
├────────────────────┬──────────────────────────────────┬───────────────────────┤
│ Existing IED       │ BROWSER LEFT: virtual TreeGrid   │ BROWSER RIGHT: grid   │
│ Explorer           │                                  │                       │
│ (global shell,     │ IED selector + search            │ Object breadcrumb     │
│ not Browser panel) │ IED                              │ Read / Read scope     │
│                    │ ├ GOOSE                          │ Inline source/status  │
│                    │ ├ Reports                        │ ┌ Attributes         │
│                    │ ├ Setting Groups                 │ ├ Last Report        │
│                    │ ├ Files                          │ └ Data/value tree    │
│                    │ ├ DataSets                       │                       │
│                    │ └ Data Model                     │ (template changes by │
│                    │                                  │ selected object)     │
└────────────────────┴──────────────────────────────────┴───────────────────────┘
```

- The **IED Explorer** on the far left may offer a collapse/compact affordance to recover width; do not add a third always-visible inspector to the Browser workspace.
- Right-side advanced inspector/properties may be **transient drawer/overlay**, not a permanent third split panel.
- At 1480 px desktop width target ~240–258 px shell, 280–330 px Browser tree, all remaining width right DetailGrid. Browser tree user-resizable with a narrow GridSplitter; suggested min 250 / max 380 px; detail min ~520 px at supported minimum window size. These are UX design starting values, to be verified at 1180 px and 125–150% display scaling.
- Use lightweight 1 px separators, restrained borders, compact table headers/rows, restrained font weight. Target 11–12.5 px body, ~25–28 px header, ~24–28 px ordinary row at 100% scaling; high-DPI/accessible scaling takes precedence over absolute pixel sizes.
- No redundant nested cards, giant title blocks, pill/badge overload, three independent vertical scrollbars or bright decorative labels. Use ARSAS application typography, colors, hover/focus consistency.
- Default two visible data columns on right: **Name / Value**. Optional **FC / Type / Source / Status** can appear in an advanced column mode without hiding semantic facts. Exact references available via selection tooltip/copy.
- Tree left visually behaves as a grid: flat row rules, type glyph, indentation, chevron, focus/selection/hover, ellipsis plus full tooltip, optional status cell, consistent header and scroll.

## 3. Navigation taxonomy (DEC-02)

One root per IED (source may be imported SCL, discovered snapshot, or currently connected IED):

```text
IED (identity, endpoint, source, connection)
├─ GOOSE
│  └─ LD
│     └─ LN
│        └─ GoCB
├─ Reports
│  └─ LD
│     └─ LN
│        ├─ URCB instance (exact identifier)
│        └─ BRCB instance (exact identifier)
├─ Setting Groups
│  └─ LD
│     └─ SGCB
├─ Files
│  └─ directory → file
├─ DataSets
│  └─ LD
│     └─ LN
│        └─ DataSet
└─ Data Model
   └─ LD
      └─ LN
         └─ FC group [optional view]
            └─ DO / SDO
               └─ DA / BDA
```

- FC grouping is a user-switchable presentation option where useful (IEDScout familiarity); the **canonical DO/DA reference is never altered** by changing grouping.
- Never invent synthetic RCB instance suffixes. Preserve indexed vs non-indexed RCB semantics and exact runtime identity.
- Distinguish user reference vs MMS reference; do not collapse `t` and `T` or other case-sensitive MMS members. Render standard names and vendor-specific extensions unaltered.
- Maintain exact **DataSet member order, multiplicity, FC and member index**; do not alphabetically re-sort or deduplicate dataset membership. Other tree groups may have an optional engineering-friendly sort as a UI-only view, not a model mutation.
- Hide/disable unsupported categories with meaningful empty states; do not draw fake children.

## 4. Core interaction contract (DEC-03)

| ID | Operator action | Required behavior | Network impact |
|---|---|---|---|
| UX-01 | Single click tree row | Select; show cached/model detail; preserve tree expansion. | **None** |
| UX-02 | Click chevron / Left/Right keys | Expand/collapse immediate children only; persist focus. | **None** |
| UX-03 | Double click | Expand/collapse container; for leaf select/show details; never invoke write. | **None** |
| UX-04 | Read selected | Asynchronous, cancellable, typed read of selected **readable** property/attribute. | Explicit read |
| UX-05 | Read scope / Read All | Bounded batch read for selected subtree, progress + cancel, failures per row. Never implicit full IED storm. | Explicit read |
| UX-06 | Refresh model/view | Reconcile UI from cached canonical model; preserve identity, no network call. | **None** |
| UX-07 | Reload/discover | Separate, clearly named user workflow; not synonymous with Refresh. | Explicit discovery |
| UX-08 | Search | Search exact IEC ref, name, LN/DO/DA, RCB, DataSet, type; display ancestor path and jump/focus result. | **None** |
| UX-09 | Context menu | Read, Read scope, Copy ref, Find in Data Model, Open linked DataSet / RCB, Expand/Collapse, Properties. | Command-dependent, clearly labeled |
| UX-10 | RCB selection | Show configuration, status, link, last observed report, ordered data members. | **None** |
| UX-11 | RCB explicit live read | Read supported live RCB fields without ownership/activation changes; show per-field freshness. | Explicit read only |
| UX-12 | DataSet selection | Show identity, order/member count, FC/MMS ref, links and cached read values. | **None** |
| UX-13 | SGCB selection | Show SGCB metadata and SG/SE setting matrix if supported/actually fetched. | **None** until Read |
| UX-14 | Disconnect/change IED | Mark prior values stale; cancel/invalidate read requests from old association; never attach old result to new IED. | No new read |
| UX-15 | Error/no support | Inline row-level status, reason, timestamp and optional diagnostics expansion. | **None** |
| UX-16 | Reopen IED | Restore model-based navigation and last selection, but reset live validity until new read/reports. | No implicit network |
| UX-17 | Keyboard | Tab focus, arrows, Enter, F5 (view refresh), Ctrl+F search, Ctrl+C copy. Read must have a deliberate binding, not ambiguous Enter. | Explicit action only |
| UX-18 | Cross-navigation | `RCB → DataSet → member → Data Model`; back navigation restores previous selection/scroll. | **None** |

**Explicit Read** is different from report subscription. A read result cannot be represented as "Information received in last Report."

## 5. Right DetailGrid templates (DEC-04)

Use one shared styling/row system, populated by typed presenters, not multiple unrelated sub-tab implementations. The right grid is allowed object-specific sections, but must share headings, spacing, expanders, error states and value formatting. Section headers: subtle row backgrounds, not giant cards.

### 5.1 Report Control Block — P0

Section **Control Block attributes**: exact RCB reference, BRCB/URCB, `RptID`, `DatSet`, `RptEna`, `Resv` / `ResvTms` when applicable, owner, `ConfRev`, `BufTm`, `IntgPd`, `TrgOps` (named flags), `OptFlds` (named flags), `EntryID` / `PurgeBuf` where present/readable, last-read state, per-field source and support status. Do not treat missing/unsupported as false or zero.

Section **Information received in last Report**: last observed report timestamp, reception time, reason(s) for inclusion, sequence number, EntryID, BufOvfl and other present optional fields. Show **No report received in this association** when true. Report fields come solely from existing received report evidence and are association/RCB-bound (not synthesized from direct MMS reads).

Section **Data**: ordered linked DataSet members; hierarchical DO/DA, value, FC, quality bits, timestamp/time quality. Enable `Open DataSet` and `Find in Model`. If only discovery/SCL configuration is available, show model members as `Not read`; do not invent report-backed values.

Navigation is read-only by default. Future `Enable RCB`, GI, reservation or reconfiguration belongs to a separately guarded action flow and is **not** in Browser P0.

### 5.2 DataSets — P0

- Show name, full IEC/MMS ref, LD, LN, member count, deletable when known, source (SCL vs IED directory), `Used by RCBs` and `Used by GoCBs` (if evidenced).
- Grid rows preserve IEC-defined index, duplicate members and FC exactly: `# | Member / structured children | Value` default, FC/type in compact metadata or optional columns.
- Expand FCD structures to structured FCDA leaves only with verified type/schema evidence.
- `Read members` is an explicit bounded operation, not a report listener or silent polling loop. Present partial read and unsupported statuses individually.

### 5.3 Data Model / Global Data — P0

- Tree follows IED/LD/LN/[FC]/DO/DA; detail shows resolved IEC ref, MMS ref, CDC, FC, type, enum label **and ordinal**, selected value, quality, timestamp, source/confidence.
- Structured children include `stVal`, `q` flags and `t` metadata as appropriate. Quality and timestamp components remain expandable and aren't split into noisy always-on columns.
- Distinguish `status only` from valid control operation capability. A control-specific dialog is never opened by an accidental double click on `Pos`.
- Missing schema or ambiguous FC resolution is rendered as `Unresolved`; do not choose a likely MMS variable and quietly claim a verified read.

### 5.4 Setting Groups — P1, engine API verification gate

- SGCB name/ref, `NumOfSG`, `ActSG`, `EditSG`, `CnfEdit`, `LActTm`, reservation/status and affected LDs, **where readable and supported**.
- Display a parameter matrix `Reference | FC | Type | Group 1 … Group N` only for actual group-specific values known through verified engine services; allow compact active-group-first mode for many groups.
- SG vs SE values and active vs editable vs preview group must remain semantically distinct. No fabricated preview values and no implicit `SelectActiveSG` or `SelectEditSG`.
- Initial delivery **read-only**. Changing active group, editing and confirm are explicitly deferred to separately safety-reviewed workflows.
- A `1970-01-01`/epoch-like invalid timestamp must be classified as `Unknown / unsupported`, not displayed as a real device change event.

### 5.5 GOOSE / SMV — P1

- GoCB: IEC reference, `GoID`, `DatSet`, `ConfRev`, `GoEna`, `NdsCom`, min/max timing, MAC/APPID/VLAN when source actually provides them.
- SVCB: `SvID`, `DatSet`, `ConfRev`, `SmpRate`, `SmpMod`, `noASDU`, enabled and addressing where available.
- **Do not conflate** configured SCL/GoCB metadata, live MMS server attributes and passively observed Ethernet frames. Show source per field or section.
- Link to DataSet and existing GOOSE Subscriber workspace; selecting does not subscribe/enable transmission.

### 5.6 Files — P1

- Root/path directory, child directory traversal, filename, size, modification time, read/transfer support and diagnostics.
- Explicit `Refresh file listing`, `Download`, `Open/Preview` when supported. File download/preview is a separate cancellable workflow with progress, bounded path safety and vendor errors; no automatic transfer on selection.
- Do not infer type solely from extension where the server does not supply it.

### 5.7 IED root / session

- Identity and identity confidence/source, address/port, SCL/discovery source, model capture time, online status and association identity.
- Model coverage counts, capability/evidence status and safe navigation to root categories. `Connected`, `SCL only`, `Disconnected (last read)` must be unambiguous.

## 6. Value semantics and provenance (DEC-05)

Each displayed value is a typed immutable presentation snapshot, with fields such as:

```text
ObjectKey        exact IED + LD + LN + category + FC + reference + instance
DisplayName      user-friendly, never replacing exact reference
DeclaredType     from SCL/discovery/verified MMS type
Value            typed, nullable / structured
Validity         NotRead | Reading | Fresh | Stale | Unsupported | Unavailable | Failed
Origin           ImportedScl | LiveDiscovery | DirectMmsRead | ObservedMmsReport | PassiveGoose | Cache
ObservedAt       timestamp from actual event/read, if known
IedTimestamp     device timestamp only if actually present and valid
SessionEpoch     monotonically increasing IED association incarnation
ModelRevision    immutable canonical model revision / generation
Diagnostic       structured failure/evidence
```

Rules:

- SCL configuration = model truth, **not** proof of live value. Discovery directory = existence/structure evidence, not necessarily value evidence.
- Last read value may remain visible offline but is **Stale** and labeled with actual last-read time.
- Report-derived current value retains RCB identity; `RptID` and RCB name are separate fields. Acquisition columns elsewhere must retain the actual active RCB **name(s)**, not `RptID`.
- Invalid `q` and timestamp quality are visible rather than filtered. Preserve IEC quality bit distinctions.
- No UI fallback that silently changes static DataSet mode to cyclic MMS polling.
- Never attach a late read result from association A to association B, even when the endpoint IP is the same.

## 7. Recommended implementation architecture (DEC-06)

```text
ARSAS Browser View (thin WPF controls)
 ├─ IedBrowserViewModel                selected IED, object, status, commands
 ├─ IedBrowserTreeProjection           flat visible-node list, search, expand/collapse
 ├─ IedBrowserDetailPresenter          reusable typed/section/leaf rows
 ├─ IedBrowserNavigationHistory        stable object keys
 └─ IIedBrowserReadCoordinator         explicitly bounded async reads
           │
           ├─ Existing ARSAS session/application adapter
           │    ├ LastLiveModel / LastCanonicalModel
           │    ├ already loaded SCL model
           │    ├ observed report state
           │    └ explicit read-only operations (no implicit new association)
           │
           └─ ARIEC61850 canonical & protocol services
                ├ MMS discovery & directory
                ├ MMS smart typed read and structured binding
                ├ DataSet directory
                ├ RCB attributes / observed report stream
                ├ file services
                └ SGCB read service only if proven / added additively
```

### Implementation guidance

1. Create a **dedicated** `Views/IedBrowser/IedBrowserView.xaml` and VM/presenter/services under corresponding folders; keep `MainWindow.xaml` to hosting + navigation, not another multi-hundred-line control composition.
2. **Use a virtualized flat WPF DataGrid as TreeGrid**: typed `BrowserTreeRow` with `Depth`, `HasChildren`, `IsExpanded`, `ObjectKey`, type glyph, readable status; expander/indent in first cell. Do not place a WPF `TreeView` inside each DataGrid row or instantiate all `TreeViewItem` controls imperatively.
3. Model hierarchy remains immutable/indexed; TreeGrid holds **only currently visible rows**, initially category + LD/LN scope. Expand inserts only immediate children; collapse removes descendant range. Provide deterministic row key and projection range updates; reuse the existing bulk observable collection infrastructure where suitable.
4. Right DetailGrid uses a **shared typed row model**, with section/attribute/value/structured-child row kinds, `DataTemplateSelector` only if measured necessary, no per-cell costly converters and no deep nested `ItemsControl` trees.
5. Avoid index-driven tab navigation: use a named workspace or typed navigation registry, not numeric `MainTabs.SelectedIndex=7` as the sole navigation contract.
6. No raw `object?` + guessed position/index parsing inside XAML code-behind. Adapter returns typed `Result<T>` / structured state with exact evidence; preserve engine semantic binder behavior.
7. Build display projections off the UI thread when materially expensive, publish immutable snapshots to UI and make small dispatcher changes only. No arbitrary `Task.Delay` fixing races.
8. Maintain one subscription owner per selected IED/VM. On IED switch, disconnect, model replacement, navigation away or shutdown, dispose stale subscriptions/CancellationTokenSources; sequence/generation guards reject late results.
9. **Protocol read concurrency:** reuse the existing approved session/read scheduler or an explicitly approved read-only association if necessary; document safety/availability and **never** open an additional association implicitly. Do not disturb report receive routing, reservation or active monitor.
10. No hidden expensive work on hover/selection; do not reconstruct entire model/detail tree on each selection. View cache keyed by `ObjectKey + ModelRevision`; invalidate only touched nodes.
11. Keep source-aware formatting centralized: IEC enum, Boolean, timestamp/time-quality, q-bits, engineering units, reference formatting, error/unsupported states.
12. Adapt/borrow ideas from `ARIEC61850/apps/AR.Iec61850.IedDiscovery/ViewModels/DetailRows.cs` **conceptually or via shared engine model**, rather than copy UI/protocol logic into ARSAS.

**Candidate code ownership (not mandatory filenames):** `Views/IedBrowser/IedBrowserView.xaml`, `ViewModels/IedBrowserViewModel.cs`, `Models/IedBrowserNode.cs`, `Services/IedBrowser/IedBrowserModelAdapter.cs`, `Services/IedBrowser/IedBrowserReadCoordinator.cs`, `Services/IedBrowser/IedBrowserDetailPresenter.cs`, with dedicated deterministic tests.

## 8. Performance, scaling and resilience (DEC-07)

- Acceptance fixtures: actual ZIV AVR model, a sparse SCL fixture, a multi-LD + multi-IED fixture, indexed/non-indexed RCB fixture, deep quality tree, and synthetic 100k+ DA stress model.
- **No full eager UI container materialization.** Cost of rendering/scrolling should track visible rows. Search index for all canonical paths may be constructed once on worker from immutable model, without UI container creation.
- Virtualization required for both TreeGrid and DetailGrid: recycle rows, ensure `ScrollViewer.CanContentScroll`, avoid outer ScrollViewer wrapping a DataGrid, and verify virtualizing panels after template changes.
- Expand only local children, preserve stable selection/top visible object on local view Refresh. Batch/coalesce high-rate report value notifications; report reception must not cause per-value full-grid rebind.
- Read scope has explicit cap/batching, timeout/cancel, per-attribute failures, loading progress, final partial-completion summary. No unlimited `Task.WhenAll` over thousands of DA.
- Match project budget: no sustained normal-interaction UI stalls >50 ms; p95 latency not >10% worse than qualified baseline without justification, with measured before/after evidence.
- Test resizing at 1180×760 minimum, 1480×900 reference and 1920×1080, 100%/125%/150% DPI, long vendor IEC references, large enum labels and Indonesian/English labels.
- Status/error in-row and non-modal; no popup barrage on individual failed reads. Accessible tooltips for exact reference, type, full error and timestamp.
- Avoid heavy box shadows, decorative cards, excessive bold 14–20 px headings inside data area. Optimize for density and reliable scanning, not a dashboard aesthetic.

## 9. Source/API readiness audit: no false completion claims

| Feature | Verified repository surface | Proposed action | Status |
|---|---|---|---|
| Canonical model LD/LN/DO/DA | `LiveIedModelDiscoveryModels.cs`, ARSAS `LastLiveModel` / `LastCanonicalModel` | adapter and tree/detail projection | Existing |
| DataSet members and order | `MmsDataSetDirectory.cs`, `LiveIedDataSetModel` | render exact member order + links | Existing |
| RCB metadata | `MmsReportInventory.cs`, `NativeIec61850Client.ProbeReportControlAsync` | typed property/detail adapter; expose flags currently discarded in some UI probe calls | Partial adapter gap |
| Explicit direct / smart MMS read | ARSAS `ReadValueAsync`, ARIEC `MmsRead` and smart read | bounded command + structured result | Existing; API/lifetime audit |
| Structured value binder | ARIEC IedDiscovery `DetailRows.cs` / `Iec61850ValueBindingEngine` | reuse stable semantic projection | Existing in engine-side workbench |
| SGCB inventory | `LiveIedModelDiscoveryDocument.SettingGroupControls` | show inventory; verify typed live `GetSGCBValues` / group matrix availability | **Capability gate** |
| GOOSE/SVCB inventory | `GooseControlBlocks` / `SampledValueControlBlocks` | source-aware detail, no invented addresses | Partial |
| File Directory & Transfer | `LiveIedFileDirectoryModel`, `MmsFileDirectory.cs` + file services | wire read-only listing, guarded download separately | Engine exists; GUI integration |
| Last observed Report information | Report receive/monitoring engine | session-bound read-only snapshot adapter (no synthetic last report) | Integration verification |
| Browser UI branch | `feat/p10-ied-browser-deferred` | reconcile latest verified screenshot build, then refactor | **Branch parity gate** |

If a capability gate fails, ship a clearly marked `Not supported / Not implemented in this build` state and log an additive engine API ticket. Do not claim IEDScout feature parity until real tests pass.

## 10. Acceptance scenarios: testable requirements

| Test | Given → When | Pass criterion |
|---|---|---|
| AT-01 | Open existing SCL, offline → Browser | Tree + detail browse without TCP/MMS and values correctly show Not Read |
| AT-02 | Complete live discovery → Browser | Same canonical navigation grammar as Open SCL, no second discovery |
| AT-03 | Select RCB `brcb01` | Control Block attributes + last report area + DataSet data region on same right pane |
| AT-04 | No report received | Last-report region clearly says so; direct read not misrepresented |
| AT-05 | Report received with fields | Exact actual report entry/seq/reason and RCB identity shown, no mixing from other RCB |
| AT-06 | Select DataSet with 58 members | All 58 in exact server order and FC, duplicates preserved, no omission |
| AT-07 | Expand DA `q` | Correct named quality flags, validity/source/test/operatorBlocked, no false Good default |
| AT-08 | Expand DA `t` | Real timestamp/time-quality, invalid/unknown explicit |
| AT-09 | Select `ATCC1.TapChg` | Structured attribute values and declared types; no control execute |
| AT-10 | Select SGCB with `NumOfSG=4` | Accurate group properties; per-group values only when read with supported service |
| AT-11 | SGCB preview service absent | Clear Unsupported; no invented group-value matrix |
| AT-12 | Select GoCB | Source-labeled fields and direct DataSet link |
| AT-13 | Select Files / directory | Directory records accurate; no download until explicit command |
| AT-14 | Select object repeatedly | Zero MMS requests, zero RCB writes, zero GI, zero file transfers |
| AT-15 | Refresh view repeatedly | Zero MMS requests; same selected object/expanded nodes and unchanged model truth |
| AT-16 | Read selected, then disconnect | Read cancelled or safely completes to old epoch and is rejected; stale labels correct |
| AT-17 | Switch IED while read outstanding | No old-IED value published into current IED; no leaked event handlers |
| AT-18 | Search collapsed subtree | Finds exact reference and can reveal/focus ancestor path |
| AT-19 | Read All large node | Bounded requests, progress/cancel and per-value partial error states; UI responsive |
| AT-20 | Different case `t` vs `T` | Distinct identifiers preserved; no silent alias conflation |
| AT-21 | Indexed/non-indexed RCB | Display exact actual instance identity, no invented `01` suffix |
| AT-22 | Dataset → Model → Back | Correct linked member selected; previous scroll/selection restored |
| AT-23 | RCB inspection with static report-only monitor | Monitoring and report-only authority unchanged; no cyclic MMS fallback |
| AT-24 | 100k DA synthetic model | No eager 100k WPF row/control creation; scroll/expand stable; measured budget evidence |
| AT-25 | 1180×760 / 150% DPI | No clipped critical columns, overlapping badges or unusable toolbar; resize retains min detail |
| AT-26 | Long IEC/MMS identifiers | Ellipsis only visually; copy/tooltip returns exact reference |
| AT-27 | Multi-IED session | Each IED retains its own source, model and read/observed-report state |
| AT-28 | ARSAS navigation regression | Existing Explorer, Live, Event Log, Alarm, GOOSE, Diagnostics, FAT and dialogs still open correctly |
| AT-29 | RCB Export / static reporting regression | No changes in SCL export selection, discovery lineage, or actual report projection |
| AT-30 | Installer smoke (when implementation exists) | Exact Windows build artifact launches, Browser entry works; tests and logs attached |

## 11. Milestone / PR segmentation and stop gates

**M0 — Ground-truth reconciliation (mandatory):**
- Identify branch+commit and package provenance for screenshot build; diff against `main` and deferred Browser branch.
- Capture reference IEDScout Global Data / Reports / DataSets / Setting Groups behavior using a checklist, no assumed functionality.
- Lock model field matrix and safe-read surface; classify each field `Ready / Adapter / Engine Gap`.
- Deliver branch selection/compatibility note. **Stop** if verified base cannot be established.

**M1 — Browser shell + virtual TreeGrid:** two panels, selection, categorization, keyboard, search, navigation identity, cached offline model, virtualized rows. No new protocol calls. Desktop visual acceptance.

**M2 — Report + DataSet + Global Data detail:** type-safe rows, RCB properties and last actual report evidence, member-order exactness, smart read expansion, q/t/enum formatting, cross-links. ZIV AVR validation. **Stop** unless M2 live values source/freshness are correct.

**M3 — SGCB, GOOSE/SMV, Files:** capability-gated read-only integration, bounded listing, group preview only when supported; additive engine PR only for verified gaps.

**M4 — QA / polish / release:** performance profile, large IED tests, branch/lifecycle regression, screenshot pixel review, Windows packaged artifact and supervised live IED commissioning evidence. No silent auto merge.

Each milestone is its own focused PR with deterministic tests and **before/after screenshots**. Keep ARIEC61850 changes in engine-only PRs and pin qualified engine lineage in ARSAS consumer PR. A green compile is not enough to claim parity.

## 12. Definition of done (DoD) / non-negotiable

- Operator can perform IEDScout-like **select → inspect → expand → explicit read → navigate related object**, all within the same two-panel Browser workspace.
- Reports, DataSets and Global Data are fully functional for tested IED/model; SGCB etc are truthfully surfaced only at supported capability levels.
- No navigation-triggered protocol write, GI, new association, polling fallback or surprise discovery.
- Every value has truthful source, last update and failure/unsupported state; last report is actual observed report.
- Confirmed visual audit at real 1180/1480/1920 widths and high DPI; compact professional typography, no clipping/collisions.
- No regression in ARSAS report-only, RCB export, discovery, connection lifecycle, acquisition labels or FAT.
- Real ZIV AVR session evidence + deterministic fixture tests + CI + installer QA before release.
- No implementation change permitted until **M0 source branch reconciliation** is explicitly complete.

## 13. References

- [ARSAS Architecture](../ARCHITECTURE.md)
- [ARSAS Invariants](INVARIANTS.md)
- [ARSAS Performance Budget](PERFORMANCE_BUDGET.md)
- [Existing deferred Browser code](https://github.com/masarray/arsas/blob/feat/p10-ied-browser-deferred/MainWindow.IedBrowser.cs)
- [Existing deferred Browser XAML](https://github.com/masarray/arsas/blob/feat/p10-ied-browser-deferred/MainWindow.xaml)
- [ARIEC61850 live discovery contracts](https://github.com/masarray/ARIEC61850/blob/main/src/AR.Iec61850/Discovery/LiveIedModelDiscoveryModels.cs)
- [ARIEC61850 exact DataSet directory](https://github.com/masarray/ARIEC61850/blob/main/src/AR.Iec61850/Mms/MmsDataSetDirectory.cs)
- [ARIEC61850 value detail rows](https://github.com/masarray/ARIEC61850/blob/main/apps/AR.Iec61850.IedDiscovery/ViewModels/DetailRows.cs)
- [OMICRON What's New in IEDScout 4.20](https://www.omicronenergy.com/download/document/B7835D19-CE42-4433-B0F6-09AE4ED01581/) — independent documented setting-group matrix/file support and large-model behavior.
