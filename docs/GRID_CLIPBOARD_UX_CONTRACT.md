# ARSAS Engineering Grid Clipboard — universal WPF UX contract

## Scope and root cause
One native WPF `DataGrid` copy behavior applies to ALL engineering tables:
read-only monitors, event logs, diagnostics, GOOSE/SV, and mutable command,
configuration, FAT, RCB selector, signal wizard and batch-write tables.
The previous clipboard class rejected `IsReadOnly=False`, so command and FAT
tables never received a Copy menu. The Event Log also lacked spreadsheet
selection, and global DataGridCell styles rendered selected cells transparent.

This fix reuses `GridClipboardBehavior` and the bounded TSV formatter.
No second clipboard subsystem, per-row handlers, protocol reads, device calls,
background synchronization, or window ownership changes.

## Two selection classes
- **Inspection / spreadsheet grids**: explicit
  `ux:GridClipboardBehavior.ExcelCells="True"` sets WPF Extended +
  CellOrRowHeader with visible, calm blue selected-cell highlighting. Enabled
  on Explorer Live Values, Global Live Values, **Event Log**, SOE details,
  diagnostics, GOOSE leaves and SV measurement tables. Built-in WPF cell
  drag and Shift+click produce contiguous rectangular selections.
- **Navigation / command / write grids**: retain Single / FullRow (or their
  existing contract). This includes Command Dock, IED tree, IED files, RCB
  selector, FAT point table, batch write, Fault Records and GOOSE publisher
  identity. They ALL have Copy, including `IsReadOnly=False`; only the
  *clipboard* capability is shared, never the selection mode.
- Event Log details reflect the **current event cell** after a mouse drag
  finishes; the drawer never changes width during a selection drag. Keyboard
  navigation remains supported, and Escape still closes the drawer.
- The IED Browser property viewer is a custom `ListView`, not DataGrid.
  Its existing Copy Value / Name+Value / Visible Properties and Owner
  information actions remain unchanged; Browser selection still governs
  exact-target Read / Write / Control operations.

## Right-click and keyboard
- Copy cell value (actual clicked cell)
- Copy selected cells / rows (Ctrl+C: TSV without headers)
- Copy selection with headers
- Copy selected full rows
- Copy visible table (only current sorted/filtered rows and visible columns)

All actions are observational. Clicking to copy does not change the selected
command, checkbox state, binding, interlock/synchrocheck flags, RCB, or IEC
connection. TextBoxes in editable cells keep normal native text-copy behavior.
Existing specialized context actions are preserved, not replaced.

## Data fidelity and performance
- WPF `DataGridColumn.OnCopyingCellClipboardContent` is the authority, with
  explicit `ClipboardContentBinding` on visual template/badge columns.
  Action-only button columns have no engineering value to export.
- Column order follows `DisplayIndex`; row order follows the current
  filtered/sorted WPF `Items` view. Composite column headers use their
  visible text, not .NET object type names.
- TSV preserves row and column gaps, quotes/escapes delimiters and multiline
  values, and guards formula-like strings on spreadsheet paste. Clipboard
  text remains raw IEC data where appropriate.
- A single explicit copy may contain up to 3,000 rows, 36,000 cells or
  512 KiB-equivalent UTF-16 text budget (256,000 code units); beyond those
  limits the action fails closed, leaving the existing clipboard unchanged.
  Larger datasets should use existing CSV export.
- No work runs per telemetry update. WPF virtualization stays enabled, no
  hidden rows are materialized. One class-level Loaded hook installs per-grid
  menus and optional selection style once, using weak grid lifetime keys.

## RCB Owner
`C0100149` decodes to `192.16.1.73`. When it matches SG7_BCU's own endpoint
it is NOT evidence identifying the controlling MMS client process. A separate
explicit Owner information action explains that limit; no global hover
provenance overlays, RCB takeover, PurgeBuf or protocol mutation.

## Acceptance
- Windows compile including every changed XAML template; full WPF STA,
  engineering, and application regression suite.
- All editable + read-only DataGrids install context Copy once, retaining
  their original selection/editor semantics and existing contextual actions.
- Every non-action template column exports real visual data.
- Event and Value Viewer: click-and-drag rectangle, Shift+click range,
  Ctrl+C to Notepad/Excel; blue selection remains legible.
- Command Dock: right-click Copy Cell/Selected Rows/Table, then verify the
  same signal, flags and controls are unchanged.
- Real Windows mouse interaction is a separate QA check; a successful WPF
  unit test is not proof that every desktop theme/driver was exercised.
- R7/R10 physical IEC interoperability remains an independent qualification
  gate; this clipboard work does not alter or bypass it.
