# ARSAS IED Browser M2 — Execution Architecture & Acceptance Record

Status: planning / handoff only. No M2 implementation or release has been approved by this document.
Base: draft PR #547, branch `feat/ied-browser-m1-virtual-tree-detail-20261013`; last verified M1.5 head `5f67cb9343535467612b2528bd5529170b04635c`; Windows workflow #38415703547 green (engine 19, application 1784, packaged 6). Recheck HEAD, PR state, CI and engine lock before coding. Public stable reporting baseline MUST NOT be replaced by the draft branch.

## 1. Product goal and nonnegotiable rules

Produce an IEC 61850 native WPF engineering browser with a shallow IED/LD/LN left navigation and a dense, hierarchical, independently expandable DO/DA/BDA/value grid on the right; selected device is owned by the existing outer IED Explorer, not a new IED selector. Open SCL and MMS Discovery converge on the **same** canonical engineering schema, then provide Browse IED, Static DataSet, Signal Selection and related tasks. No competitor product/trademark names anywhere in public source, docs, tests, PR, commit messages, tooltips or release notes.

Protect verified static-report-only acquisition: never implicitly enable RCB, issue GI, write, Operate, create dynamic DataSets, substitute cyclic MMS polling, or alter report planner merely by opening Browser, expanding nodes or reading files. Explicit MMS Read is read-only; state changes require separate, existing explicit workflows.

## 2. Source audit anchors

- `SclSignalSelectionModeWindow.xaml(.cs)`: current task-first chooser. Its two monitoring choices map to `UseStaticDataSet` Boolean and `DialogResult = true`; engineering/offline actions map to false. This must be replaced by an **explicit typed task result**, while preserving existing cancel semantics.
- `MainWindow.SharedSclWorkspace.cs`: `PromptSclSignalSelectionMode`, `ApplyStaticDataSetSelection`, source-neutral static authority. Multi-IED imports need deterministic target resolution through existing IED Explorer, never an extra dropdown.
- `MainWindow.SclQuickActions.cs`: card-scoped action routing; `OpenIedWorkspaceActionsAsync` currently branches on `dialog.UseStaticDataSet`; no browse action should be interpreted as manual monitoring.
- `MainWindow.IedBrowser.cs`, `Models/IedBrowserPresentation.cs`, `Models/IedBrowserStructuredDetails.cs`, `MainWindow.xaml`: value binding, navigation, reusable row projections; current splitter `GridSplitter Grid.Column=1 Width=5 Background=#D7E1EC` and detail grid width = 50/50 by default.
- `Models/IedBrowserLiveValues.cs`, `Services/Iec61850MonitorRuntime.cs`, `Services/Iec61850ValueFormatter.cs`: existing per-IED string ledger and process-formatting logic; `FormatOperatorDbpos` and bit-string parsing already exist. DO NOT duplicate decoders in Browser.
- `Models/IedBrowserReadPlan.cs` and `Services/IedBrowserIsolatedReadService.cs`: explicit read plan, independent MMS read association. Existing device-busy/missing-endpoint limitations require clear UI states rather than silent disabled controls.
- `Services/FaultRecordTransferClient.cs`, `FaultRecordWindow.xaml.cs`, `FaultRecordWindow.ComtradeOpen.cs`, `Services/ComtradeRecordResolver.cs`, `Services/ArdIrecViewerLauncher.cs`, `Services/ArdIrecNativeBridge.cs`, `ComtradeWorkspaceWindow`: existing FileDirectory, bounded transfers, local record completeness, pairing and native COMTRADE viewer. Reuse, extract narrow shared coordinator only if needed.
- `docs/ARSAS_IED_BROWSER_LIVE_VALUE_ARD.md` remains M1.5 design record. Reconcile M2 and existing architecture invariants and performance budgets before implementation.

## 3. Canonical model and typed live state

Create one **per-IED Model Context**, not a god-class:
(A) immutable/versioned **Engineering Schema** from SCL or discovered MMS: LD, LN, DO, DA/BDA, FC, CDC/bType, DataSet membership, RCB/GoCB/SGCB and file metadata; schema identity must be stable and FC-qualified;
(B) **Typed Observation Store** per IED/association: typed value, raw evidence, quality flags, timestamp + time-quality flags, received-at, provenance (report/RBC identity or MMS read), validity/stale/error status, monotonic revision; 
(C) **Presentation Service** for operator-friendly rendering and engineering units;
(D) **Event History** independent of snapshot changes; no synthetic SOE for manual Read;
(E) RCB runtime/ownership and remote file catalog as separate bounded-lifecycle projections.

Decode at the ARIEC61850 ownership boundary, reuse available formatter and decoder behavior; verify raw value is actually Dbpos via schema before mapping `bits(40,unused=6)` to `Open [01]` and `bits(80,unused=6)` to `Close [10]`. Include Boolean, enum, q validity/detail/source/test/operatorBlocked and t/UTC/time-quality. Never guess absent flags. Preserve raw bytes and typed fields; do not store only `ToString()`. No hardcoded prefixes/vendor heuristics as identity authority; prebuild exact reference + FC lookup maps, model version key and DataSet-member maps. Alias mapping only model-verified. Design O(1) or O(depth) lookup per update; no O(model size) scan on each report or render.

Feed actual report-included members into typed store **before** user SCADA selection filtering; preserve report inclusion semantics: an enabled RCB is not proof every member has arrived. An explicit Read updates observation channel only, with source/time ordering so older manual snapshots cannot overwrite newer authoritative report state. Existing Explorer, Live Monitor, Browser, Event Log and FAT consume the same typed evidence/projection, but Event Log keeps only real report events and SCL export uses schema only. Disconnect: retain schema, mark live values stale/offline rather than advertising as fresh; no cross-association leakage. UI batches changes and updates only visible virtualized rows, preserving scroll, expanded nodes and selection.

## 4. Quick Start — second primary action: Browse IED

Replace current 2-choice boolean outcome with an explicit result e.g. `StaticDataSet`, `BrowseIED`, `SelectSignals`, `RcbEngineering`, `DownloadComtrade`, `Cancel`; the owner dispatches the selected task exactly once after the dialog closes. Keep existing working static path unchanged. New primary actions on the first row: **Static DataSet** and **Browse IED**, equally prominent; **Select Signals** becomes secondary, while RCB Engineering and Download COMTRADE remain present and easy to find. Keep compact, consistent WPF dialog styling and natural window height.

- **Open SCL:** after successful model build, choosing Browse IED opens Browser and selects the imported/current IED in existing IED Explorer; schema browsable immediately **offline**; values clearly Not read/Offline. Offer `Connect & Read` on user request if usable MMS endpoint exists; never fabricate online values or auto-enable reporting. For multiple imported IEDs, enter the workspace and let the existing IED Explorer select target; do not add a selector.
- **Discovery IED:** after valid MMS model construction, offer the same task-first choice; Browse IED immediately selects discovered IED and navigates Browser, retaining association if available and offering a bounded context Read. Never start static report by choosing Browse.
- **Card Actions:** reuse the same result handling pinned to clicked IED, no global selection races; cancel and closing chooser have no side effects; static/manual flows remain untouched.
- Auditing must locate and cover every post-Open-SCL and post-Discovery entry point, including cached model re-open and partial/offline errors.

## 5. Right-side hierarchy and RCB/Read workflow

Dense, row-virtualized WPF grid for Data Model, DataSets, Report.Data, GOOSE.Data and SG settings. Left tree does not grow below LN. Right pane supports single-click select, inline chevron expand/collapse for DO/DA/BDA/q/t (lazy children), double-click folder drill-down when appropriate, Back history, preserved row expansion and scroll on live updates, keyboard expansion, and resizable Name/Value column (initial name width ~40–45%). Never rebuild entire tree per sample; do not display raw bitstrings or `Not exposed` when typed q/t evidence is actually available.

RCB property groups (`Control Block attributes`, `Information received in last Report`, `Data`) collapse/expand. `RptEna` shows true/false **only with live proof** (observed attribute or successful local activation); otherwise `Unknown / Not checked`. Retain compact distinct BRCB/URCB badges and proven own-vs-other usage, respecting MMS availability evidence.

Read selected page is an explicit finite read, Read All is an explicit model-wide finite operation, cancellable with per-leaf progress and timeout; no uncontrolled mass reads on dialog open. A separate read-only association may be used during streaming; if IED refuses it, provide actionable explanation, don't disturb active report. If disconnected with valid model/endpoint, offer connect-then-read. If impossible, show status/reason in UI; don't leave an unexplained permanently disabled control. When Busy due to other mutating lifecycle work, defer or explain rather than racing a device association. Progress should remain compact on existing IED Card, without obscuring streaming.

## 6. Files browser and native COMTRADE auto-open

Right Files view is a **3-column DataGrid** with headers `Filename | Time | Size`, compact single-line rows, appropriate ellipsis/right-aligned size, sort, stable selection, large-list virtualization. Keep metadata current from the same ARIEC FileDirectory/fault-record catalog.

Double-click any supported COMTRADE member (`.cfg`, `.dat`, supported companions) performs:
1. Resolve exact remote record and companion group, check trusted local cache/record completeness (at least CFG and DAT; include companion files required by existing downloader), verify file sizes when known.
2. If complete: open immediately in **existing native ARSAS COMTRADE Viewer**.
3. If not complete: automatically download the **required full record** once, through existing `FaultRecordTransferClient.DownloadAsync`; show centered, refined and visually quiet overlay (filename/record + slim progress bar or indeterminate state + Cancel, no spam, no alert waterfall). Keep the list visible underneath. Use shared per-record operation/deduplication to prevent double-click duplicate transfer.
4. Write into safe temp directory and finalize atomically after successful verification; reject unsafe remote paths, traversal and unsupported sizes; cancel/failure must not mark cache complete; bounded timeout/retry according to existing transfer policy; no UI thread blocking.
5. Resolve CFG and open via existing ArdIrec native bridge / `ComtradeWorkspaceWindow` off the UI thread, maintain resource ownership/disposal.
6. Context menu **Download** downloads to existing user-selected destination (do not conflate with transient auto-open cache); if already cached, offer intelligent reuse/copy. Unsupported file types may show compact unsupported-preview status and still permit download, never feed non-COMTRADE blindly to native viewer.
7. If a second MMS association is refused, fail gracefully and preserve reporting. Avoid duplicate FileDirectory/service implementation.

## 7. Minimalist splitter visual

In `MainWindow.xaml`, change the current 5px solid gray separator to a ~1px visual line inside a 6–8px **transparent hit target** for resizing. Idle: subtle neutral line. Hover/drag: dark charcoal/black, visually 2–3px thick (smooth but inexpensive state trigger), proper cursor and keyboard accessibility. Preserve adjacent panel width minimums and resize functionality; avoid a tiny unusable hit target, extra cards or bulky font.

## 8. Delivery milestones and release gates

M2.0: Audit/ARD/checkpoint — identify correct ARSAS PR, ARIEC engine lock/ref, verified stable reporting baseline, touched UI/report/file paths, test inventory; document ownership, risks, migration policy. No refactor yet.
M2.1: ARIEC typed quality/time/Dbpos decoder and shared formatter with golden real captured frames and MMS Read fixtures; tests for all 4 Dbpos states, q flags, UTC time-quality and unrecognized data. Engine tests and baseline ARSAS tests green.
M2.2: Typed per-IED observation store & immutable schema adapter with indexed O(1) mapping, observed-vs-cached semantics, cross-tab parity tests, disconnect/reconnect and concurrent updates. Preserve SCL export and static report-only.
M2.3: Shared right hierarchy and RCB collapsible details, width/visual adjustments, minimal hover splitter, virtualization/performance tests and screenshot QA.
M2.4: Quick Start typed-action routing, primary Browse IED, Open SCL + Discovery parity and Read usability/state machine. Explicit static-report pathway unchanged; test cancel and multi-IED.
M2.5: Files 3-column grid, context Download and double-click auto-download/progress/native COMTRADE open, deduplication, cleanup and interoperability regression.
M2.6: ZIV AVR + BCUGE physical acceptance, screenshot comparison, performance/fault injection, ARIEC pin + Windows portable EXE artifact; keep PR draft until acceptance and do not publish stable without explicit approval.

Every milestone must be independently reviewable, with small PR/commits or clean checkpoint, exact git SHA, green Windows build and regression tests, evidence of what is truly verified, remaining limitations, and a valid GitHub artifact when GUI field testing is requested. Never delete/overwrite progress on the last verified release or merge feature work into main prematurely. No superficial test assertions in place of runtime evidence.

## 9. Field acceptance matrix

A. ZIV AVR LLN0.DS_Signal: `ATCC1.TapChg`, `CSWI1.Pos`, `q`, `t` values agree on Browser, Live and Explorer; `bits(40,unused=6)` => `Open [01]` only when verified Dbpos, `bits(80,unused=6)` => `Close [10]`; no raw protocol text visible by default. q detail flags are decoded when raw evidence exists, otherwise genuinely Unknown, not default false.
B. Data Model ATCC1: DO/DA/BDA expand/collapse in place, showing live value, FC and nested siblings, and preserving scroll/selection during report traffic.
C. RCB: actual RptEna true/false or explicit Not checked, three collapsible groups, last-report information if observed, usage markers and buffered/unbuffered distinction correct.
D. Read/Read All: completed/failed/cancel paths, when actively reporting/idle/disconnected, under single-association IED behavior, no report interruptions and no synthetic events.
E. Open SCL & Discovery: `Browse IED` second primary action reaches same Browser workflow without enabling monitor, and existing Static DataSet remains unchanged.
F. Files: correct Filename/Time/Size columns; double-click already-downloaded opens natively, not-downloaded triggers calm overlay and validated full-record auto-transfer then opens; cancellation/retry/unsupported files do not produce partial-broken viewer state; context Download works.
G. Splitter: hairline idle, dark/thicker hover, 6–8px drag hit area, resizable and usable across window sizes.
H. Performance: virtualized large LD/LN/DO/DA catalogs; report throughput and UI responsiveness equal to or better than verified M1.5, no N×N scans, association leaks or long UI-thread blocking.
