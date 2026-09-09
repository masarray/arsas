# ARSAS FAT Engineering Workstation Contract

This document is the architectural freeze for the Engineering-integrated FAT workflow. Changes that violate these invariants must not be merged, even if a visual patch appears to fix one field symptom.

## Ownership

- `MainWindow` owns the persistent Engineering workstation shell, navigation, global IED Explorer, and shared Command Dock.
- Engineering owns IEC 61850 device identity, `SelectedDevice`, parsed `SclIedWorkspace`, static DataSet selection, live signal definitions, live points, MMS/report acquisition, and connection lifecycle.
- The production FAT controller owns FAT preflight, session lifecycle, capture semantics, V1/V2 evidence, completion/result semantics, and FAT persistence.
- FAT presentation must not own a second IEC 61850 parser, discovery session, MMS connection, live polling loop, or command runtime.
- Report presentation must reuse the production FAT report definition; preview and PDF/print must not diverge into separate report models.

## Primary user flow

`Open SCL -> Connect/monitor IED -> select IED in persistent Explorer -> FAT`

The first visible FAT frame must be the production FAT workspace for the selected Engineering IED. The primary flow must not require `Open SCL for FAT`, a launcher card, a second SCL parse, or a separate operator-visible FAT window.

## Navigation invariant

Engineering navigation has exactly seven destinations in this order:

1. IEC 61850 Explorer
2. Live Monitor
3. Event Log
4. Alarm
5. GOOSE
6. Diagnostics
7. FAT

There must be one navigation owner. Do not reintroduce a second `ModuleInitializer` / `ApplicationIdle` parity layer that fights the canonical navigation state.

Target end-state: the FAT button and FAT `TabItem` are literal XAML siblings of the other six destinations and use the same `SegmentedNavButton` style resource.

## Multi-IED context invariant

When FAT is idle, the viewed FAT IED follows Engineering `SelectedDevice`.

When a FAT session starts or continues, the capture target is latched to that session IED. Changing the Explorer selection may change the viewed IED but must never redirect an active capture/evidence transaction to another IED.

Persisted FAT state is reconciled by stable device/IEC identity. Background reconciliation is read/reconcile-only and must not manufacture new evidence.

## Static DataSet authority invariant

Automatic Engineering FAT uses the authoritative static DataSet projection from the already parsed `SclIedWorkspace`.

Synthesized `scl-manual-*` aliases must not be allowed to create duplicate enabled ownership of the same primary live leaf in automatic static mode. The strict duplicate-live-reference preflight guard remains enabled; invalid duplicate scope is cleaned up rather than weakening the guard.

Manual-only projects keep their manual-row behavior.

## FAT presentation invariant

The final Engineering FAT center must reuse the exact production FAT grid presentation rather than an approximation or a second native FAT grid implementation.

Target reusable structure:

- `FatWorkspaceView`: canonical production grid/workflow presentation.
- `FatReportPreviewView`: canonical production A4 preview presentation.
- `IoListTestingWindow`: legacy standalone shell that may host the reusable views for compatibility, but must not be the presentation/lifecycle owner of the Engineering FAT tab.

The Engineering FAT tab must eventually own a permanent FAT visual surface. Selecting FAT should only switch workstation content; it must not require `Window.Show -> Loaded -> detach/reparent`.

## Report preview invariant

Default center = production FAT grid.

`Print/Report Preview` switches the same FAT center to the canonical paged A4 preview. The persistent IED Explorer and shared Command Dock remain visible. Returning from preview restores the same grid instance/state, including selection, scroll position, V1/V2 state, and active session context.

## Protected production behavior

Refactoring presentation must not change:

- Select/SBO/Operate sequencing or control safety behavior.
- `ctlModel`, interlock/synchrocheck/Test, CommandTermination semantics.
- RCB runtime behavior.
- GOOSE capture.
- MMS/report/live-monitor acquisition semantics.
- Production FAT preflight, auto-capture, evidence, completion/result, and persistence semantics.

## Delivery gates

A milestone is not called fixed until it has three statuses:

- CODE VERIFIED
- CI VERIFIED
- FIELD VERIFIED

Once a field gate is accepted, later milestones should not change that layer unless a regression test demonstrates a real dependency.

## Current migration order

1. M0 Architecture freeze and regression locks.
2. M1 Canonical seven-destination shell/navigation.
3. M2 Permanent FAT surface; remove primary launcher/black transition.
4. M3 Selected-IED view synchronization.
5. M4 Production controller adapter and latched capture context.
6. M5 Evidence/persistence reconciliation.
7. M6 Canonical in-place A4 report preview.
8. M7 Remove donor-window/parity/experimental duplicate presentation code.
9. M8 Physical-IED acceptance.
