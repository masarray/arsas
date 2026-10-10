# ARSAS / ARIEC61850 — C264 same-host simulator field handoff (2026-10-10)

> **New implementation checkpoint:** [ARSAS draft #504](https://github.com/masarray/arsas/pull/504) introduces the *P1-only* editable-IP and Fault Records alignment fixes, with regression tests, on the exact #501 baseline. Initial HEAD `f90f4697fb1ee607bb5500119f6b5298ce01879f`. Windows CI/visual testing must still be checked at the latest HEAD; the report-projection and GOOSE same-host defects are NOT fixed by #504. Avoid parallel duplicate P1 work. Keep this handoff and the fuller [#502 plan](https://github.com/masarray/arsas/pull/502) for P2/P3.


**Status:** engineering strategy and evidence checkpoint ONLY. This document does **not** implement the four outstanding fixes and is **not** field acceptance or merge authorization.

## Start here: operator context and observed evidence

- Test bench: **IEDScout Server is simulating an Alstom C264 on the SAME Windows PC as ARSAS**. Do not assume an external Ethernet relay or that the MMS IP route identifies the packet-capture NIC. Correct handling of a software/virtual adapter is required.
- Latest C264 Explorer screenshot: connected to **192.16.1.13:102**; 207 signals shown, including values/quality with `Unknown` / `Static DataSet report pending`, and some visible control values. The separate Add IEC 61850 IED dialog shows previously selected **192.16.1.33**; **do not conflate these endpoints**. NTP header shows PC IP **192.168.1.4** (also not the C264 endpoint).
- GOOSE card CTA navigates to GOOSE tab, but selection remains empty, capture shows **STOPPED / zero frames**. Routing hint identifies `KM_Test (Microsoft KM-TEST Loopback Adapter #2)`; default adapter filtering is suspicious.
- Add IED dialog: editable IP field text is not horizontally and vertically centered.
- IEC Explorer: some values are unknown/pending; inspect model/report state and on-wire evidence before classifying as broken.
- Fault Records window: the **select-all checkbox in the grid header** is not aligned with checkboxes in rows (preserve tri-state/bulk-selection behavior).
- Demonstrated positives to protect: C264 connects, control UI and fault-record retrieval operate; previous F650 static reporting, GOOSE decoding and NTP have validated steps. Never regress them or activate unsafe control actions just to test read-only workflows.

## Repositories, branch graph and verified baseline

**Consumer:** `masarray/arsas`; **engine:** `masarray/ARIEC61850`. Audit fresh GitHub metadata before any edit. As checked here:

| PR | Purpose | Head SHA | State |
| --- | --- | --- | --- |
| ARSAS #494 | P7.6F whole-FCD phases | `3cf821428efd954e415e7455acbd2936ff0019cd` | draft |
| ARSAS #497 | P7.6G DataSet-first ST/MX closure | `e0d63a9b8cd1dfb4b6c8321044363a283014b329` | draft |
| ARSAS #498 | GOOSE CTA, NIC and retransmission UX | `67266f2017339159e0af4812e4c40339fadd5c70` | draft |
| ARSAS #499 | GOOSE engineering values/immutable inspector | `a6d788d789eac4083ca7002b739b2f9d28d098a8` | draft |
| ARSAS #500 | standalone SNTP | `5e46dd562fc1f19f34eddc493b8c9872cb37bae6` | draft |
| ARSAS #501 | NTP visual tokens/heartbeat | `34d8f55f499be94cc49539ba632aafa6efdef105` | draft |
| ARIEC61850 #157 | DataSet-backed LN discovery | `8b0c0f7e695b016834e40a6f068994dcc945cb1a` | draft |

The last verified **cumulative consumer candidate** is ARSAS #501 HEAD `34d8f55f`: [build #38020243949](https://github.com/masarray/arsas/actions/runs/38020243949), 1,467/1,467 application regression tests PASS, Windows portable publish and executable smoke PASS. [Artifact](https://github.com/masarray/arsas/actions/runs/38020243949/artifacts/11657749906). These are build results, **not** C264 physical acceptance.

**Branch topology is not a simple PR-number chain:** #497 bases on `fix/493-f650-whole-fcd-phase-values`, #498 bases on `integration/495-smart-dataset-closure`, #499 bases on #498 branch, #500 on #499, #501 on #500. Confirm ancestry, exact pins, and potentially divergent work with GitHub compare before rebasing or choosing a new branch. Never build from old `main` by assumption or overwrite draft heads.

Mainline readiness/physical gates remain closed. Do not bypass; do not merge or create public release until field validation.

## Findings and hypothesis A — context-aware GOOSE adapter selection (highest priority)

Relevant source:
- `MainWindow.IedGooseQuickStart.cs`: `OpenIedGooseSubscriber` (around 106), `ResolveGooseAdapterForIed` (around 148), `ResolveLocalIpv4ForTarget` (around 202), `CaptureAdapterMatchesNetworkInterface`, `LooksLikeLoopback` (around 238).
- `MainWindow.GooseSubscriber.cs`: `SelectedGooseAdapter`, `StartGooseSubscriber_Click`, `ConfirmIedGooseAdapterSelection`, `RefreshGooseAdapters` (around 268).
- `Services/GooseSubscriberRuntime.cs`, `Views/GooseSubscriberLiteView.xaml(.cs)`, `MainWindow.GooseTimeline.cs`, `docs/GOOSE_SUBSCRIBER.md`.

**Specific likely defect:** `LooksLikeLoopback` searches adapter name/description/friendly name for the string `loopback`, conflating a genuine Npcap loopback pseudo-interface with Microsoft's **KM-TEST Loopback Adapter**, which may be the correct Npcap-capturable virtual interface for the same-host test bench. `ResolveGooseAdapterForIed` first computes an MMS route via a UDP socket to target:102 and then rejects even a unique matched adapter if `LooksLikeLoopback` returns true. The fallback similarly filters it out. This plausibly explains GOOSE opening with a blank adapter despite a connected C264.

**Do not naively delete all loopback checks.** Introduce typed/interface-identity classification (Npcap pseudo-loopback vs Windows virtual/KM-TEST NIC vs actual physical NIC), robust Npcap adapter matching by authoritative ID/GUID and MAC where meaningful, and visible evidence for why an adapter is chosen or rejected. MMS routing is a hint, not proof of Ethernet 0x88B8 reachability. Prefer a previously **verified successful** adapter for the same device/context; otherwise consider a small bounded cancellable *read-only* packet observation probe only on plausible adapters if technically supported and safe. No indefinite capture, broadcast scan or automatic multi-NIC traffic logging. When no confident choice exists, show adapter picker with a short actionable reason. Never show a capture-success state without observed frames.

**Acceptance:** Click C264 card GOOSE → correct adapter selected and read-only capture begins if evidence supports it; for same-host KM-TEST virtual NIC, do not exclude solely based on substring. Valid frames produce stream counter, event list, DataSet inspector. If no packets actually exist on a capturable NIC, say so honestly. Stop/change adapter/restart is reliable. Test real Npcap pseudo-loopback, KM-TEST adapter, multiple NICs, no adapter, non-broadcast GOOSE, prior verified adapter, and incorrect MMS route. GOOSE physical layer and MMS unicast IP are separate.

## Findings and hypothesis B — missing live value/quality in C264 Explorer

Source: `MainWindow.xaml` Explorer columns; `MainWindow.ExplorerSignalGrid.cs`, `MainWindow.StaticReportEvidence.cs`, `Services/NativeIec61850Client.StaticDataSetReporting.cs`, `Services/NativeIec61850Client.CanonicalAcquisition.cs`, `Services/NativeIec61850Client.SemanticReporting.cs`, `Services/StaticDataSetReportProjectionAccumulator.cs`, `Services/ReportProcessValueSafety.cs` and signal model projections. Engine ARIEC61850 #157 only if evidence proves protocol/model defect.

**Important distinction:** SCL describes signal structure, but is not live process traffic. `Unknown` quality and `Static DataSet report pending` may be correct until an exact matching report value and quality arrives. However unresolved live coverage after verified GI/report may be a real defect. Distinguish *not subscribed*, *not covered*, *waiting for GI*, *report sent but failed route*, *quality truly absent*, *quality invalid*, *unmapped semantic descendant* and *stale* through typed per-point evidence. Never fabricate good quality or a timestamp.

Investigate the actual 207-point C264 model and DataSet entries, RCB mapping, association, GI/report ingress and FCDA/FCD expansion; measure exactly how many reported process values are safely projected versus pending and why. Preserve strict Static DataSet **report-only** semantics: no association-scoped dynamic DataSet writes or cyclic MMS value polling as a hidden “fix.” Compare Open SCL and Discovery routes on canonical identity, not just displayed point count. Avoid treating C264 simulator as real instrument-quality evidence.

Potential additional UX regression: `MainWindow.ExplorerSignalGrid.cs` currently searches a grid with **six** headers (Signal, IEC Telegram, Value, Quality, IED Timestamp, Acquisition), while `MainWindow.xaml` actually shows **seven**, including Alarm. Verify whether this silently prevents compact column-fitting before changing widths. Use one canonical grid contract / named grid instead of scanning visual tree heuristically.

**Acceptance:** no fake values; every pending/unknown has an explainable evidence category; known report-backed values/quality update after actual report, remain stable; Open SCL vs Discovery semantic identity parity; F650 WYE/DEL phase reporting and GI remain correct.

## Findings and hypothesis C — Add IED editable IPv4 field center

`IpConnectWizardWindow.xaml`, `Styles/IedTaskDialogStyles.xaml`, relevant code-behind. `RelayIpBox` is an **editable ComboBox**, 40 px high, with historic IP items and LostFocus text binding. Need horizontal+vertical centering of the actual `PART_EditableTextBox` text and the selected/popup item text, not just aligning ComboBox chrome. Keep caret, selection, popup arrow, keyboard, paste, validation, recent-IP selection, DPI (100/125/150%) and narrow window intact. Prefer scoped typed WPF template/style and shared design tokens, not a global ComboBox override or hard-coded TextBox overlay. Preserve test of any user-edited IP: do not auto-correct it silently.

**Acceptance:** 192.16.1.33 and longer valid IP display centered both axes, editable with keyboard/paste, popup selection works, port 102 unchanged, buttons functional.

## Findings and hypothesis D — Fault Record header checkbox alignment

`FaultRecordWindow.xaml` DataGrid first `DataGridTemplateColumn Header="Get" Width="48"` and centered row CheckBox; `FaultRecordWindow.HeaderSelection.cs` dynamically replaces column.Header with select-all CheckBox (around 56–84) set HorizontalAlignment/VerticalAlignment Center. Visually these appear misaligned; inspect **DataGridColumnHeader content presenter padding, grid-lines, row cell padding and checkbox template** as distinct coordinate systems. Fix header/cell centering with matched templates/consistent CSS-equivalent WPF alignment; keep tri-state/mixed selection, download eligibility, bulk actions and event lifecycle unchanged.

**Acceptance:** select-all center x agrees with checkbox row center x at tested DPI and resize; clicking header selects/deselects only eligible rows; mixed state and downloaded rows remain correct; no lost click handlers.

## Execution strategy — implement, prove, prevent regression

1. **Baseline inventory and preservation:** fetch both repos and active draft PRs, exact SHA and engine pin; create a dedicated fix branch **from verified cumulative #501 head**, never from stale `main`. Record commit graph, baseline workflow, file diff and tests. Keep engine unchanged unless wire evidence demands a separate engine PR.
2. **Instrument then fix GOOSE:** add deterministic interface classification and injectable adapter matching/selection policy (pure C# unit tests). Prove failure with synthetic Npcap loopback/KM-TEST/physical NIC fixtures before code change. Reuse existing stream workers and bounded UI dispatcher. Test actual same-host Npcap capture against simulated C264.
3. **Canonical report-evidence investigation:** isolate mapping/quality phases and add targeted tests. Fix a specific verified defect only after diagnostics prove it; do not turn a pending DataSet into a fictitious live value. Guard static reporting, GI and F650 parity.
4. **Scoped GUI polishing:** editable IP template, fault-record header checkbox and Explorer grid column contract, each backed by WPF behavior tests and screenshot acceptance, no bulky cards/text.
5. **Optimization:** event-driven/coalesced updates, bounded caches, canonical lookup maps, cancellation, no unchecked retries/extra polling, no expensive visual-tree scan per frame; no unmanaged code rewrite. Distinguish work done on engine vs consumer.
6. **Validation:** exact HEAD Windows compile, complete regression, native x64 single-EXE publish + smoke, relevant field/read-only C264 capture; provide PR, precise artifact URL and known limitations. Keep all guard statuses honest; mainline readiness/physical acceptance must not be bypassed.
7. **Merge discipline:** stack new PR safely; avoid force-pushing/rebasing shared heads or merging drafts without operator approval; before release compare cumulative work and preserve all verified features (Discovery, Static Reports, GOOSE, SNTP, FAT, Fault Records, Control).

## Thread handoff success output

Report: exact repos/branch graph and verified baseline; 4 findings with confirmed root cause vs hypothesis; changed files/commit SHAs/PRs; regression and Windows CI outcomes; artifact exact-head URL; C264 simulator actual capture evidence; unresolved issues and next-step acceptance. Don't claim GUI correctness from unit tests or simulator evidence from screen mockups.

Operator priorities: **smart, context-aware, light/optimized, reliable, visually compact, electrical-engineer friendly; no naive patches; avoid native code additions and avoid breaking already working IEC 61850 control/report/clock features.**
