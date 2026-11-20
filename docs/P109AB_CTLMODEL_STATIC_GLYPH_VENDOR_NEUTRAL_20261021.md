# ARSAS P109AB — vendor-neutral command catalog and persistent ctlModel glyph (2026-10-21)

## Frozen baseline and cross-thread isolation

- User accepted field evidence ZIVAVR at ARSAS 1.6.40 portable PR #649 exact consumer SHA `4c232184f64584308396ea18ff6c82700ac02d29`, tested by Windows CI #39436937170 (source-tree-identical merge `0c42a3066b90cab4a53abd4ee674d04cb6d89191`; 2,422 consumer/24 engine/6 package tests plus native and portable smoke green).
- Immutable acceptance branch: `checkpoint/arsas-ziv-control-gocb-field-accepted-20261021` at `4c232184f64584308396ea18ff6c82700ac02d29`.
- This phase is stacked on PR #649, **not main**, separate from leaf-data branches/engine repo. No changes to ARIEC61850 or runtime MMS I/O, ctlModel writer, GOOSE, discovery, report, Data Model, transport, control-safety gates or pinned dependency SHAs.

## Technical diagnosis

ZIVAVR five StatusOnly selected CO roots are not vendor-specific exceptions: `Iec61850ControlIdentity.DistinctSelected` normalizes **full DataObject references** and filters `IsSelected && IsValidControlObject` without brand/IP checks. `DistinctOperable`, typed live CDC/ctlModel/status/ctlVal semantic gates, and exact single operator-triggered CF Write/readback remain independent. The same projection works for arbitrary IED domains, e.g. ZIVAVRLD1, SIEBCUCTRL, C264CONTROL.

Field screenshots show the gear present only on some `Model` cells. Cause: `P2IndustrialWorkstationUx.DecorateTextButtons` scanned realized visual tree and assigned `MaterialSettingsAction` DataTemplate for text `Model`; `CommandDataGrid` recycles and virtualizes containers and may realize new rows **after** one-off decorator. That creates inconsistent gears between rows and navigation. Fix removes dependence on the scan for this control: an immutable global `CommandDockCtlModelButton` WPF style owns the `MaterialSettingsAction` template, with the existing shared `MaterialSettingsIcon` path. The button content is `ctlModel`, with no runtime template assembly, no second icon, and static sizing for the larger label. The existing Button.Click and association-based IsEnabled binding are unchanged.

The old `MainWindow.CommandPanelUx` condition `or "Model"` was unreachable because `IsCommandActionButton` excludes Model. Remove the dead branch instead of turning caption checking into an authorization mechanism.

## Optimization without changing command behavior

`Iec61850MonitorDevice.RefreshCommandSignals` retains exact selected identity and incremental reconciliation. The hot **unchanged-membership** case now performs an O(N) reference-identity compare and immediately returns: no per-row `.Any()`, no DataGrid collection notifications, no resets and no UI row churn. Membership removal on actual edits uses a reference-identity HashSet instead of O(N²) repeated scans. The rare reorder pass still performs minimal granular Move/Insert; no wholesale ReplaceAll/Reset. This is a consumer presentation optimization, not an engine rewrite.

## Regression / acceptance contract

- Declarative model action must be exactly `ctlModel`, visually one 14px settings gear beside the caption on every recycled row; same appearance when scrolling / switching tabs / changing IED and at 100%-150% DPI. Template is authored statically, not in Loaded-time decoration.
- WPF ControlDetails_Click still opens the same exact live ctlModel editor only on connected IED. A Stage/Operate/SBO remains fail-closed unless typed runtime authority permits it; read-only/status-only remains visible, not physically operable.
- Large (240 selected controls) identical projection repeated 40x has zero collection notifications and preserves object instances; genuine selection change uses targeted removals, never Reset. Three different domain prefixes all work the same with no vendor exception.
- Exact head Windows CI (full app, pinned engine and native bridge tests, packaged portable smoke) before artifact handoff. **No main merge/public release** until operator confirms visuals/field safety; no claim about physical command from CI.
