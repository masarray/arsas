# ARSAS Engineering Grid Clipboard — UX and Safety Contract

## Goal
One reusable, read-only clipboard facility across WPF workspaces. Users may
right-click to copy a cell, selected range, full selected rows, or the *current
displayed table*. Ctrl+C follows spreadsheet behavior for selected cells.
The IED Browser details pane is a virtualized ListView rather than a DataGrid;
it supports copying its selected value and visible property rows.

## Two selection classes
- **Data inspection**: opt-in `ux:GridClipboardBehavior.ExcelCells="True"`
  enables WPF Extended + CellOrRowHeader rectangular selection on Explorer
  live values, Global live values, SOE details, GOOSE leaves, diagnostics and
  SMV measurement tables. Ctrl+C pastes as tab/newline-separated cells *without*
  headers; right-click supports selection with headers.
- **Identity / navigation / command**: preserve existing selection mode
  and row-click workflow. IED tree, files, RCB selectors, event-log/detail
  navigation, command/configuration/write grids and stream selectors must
  **not** be globally switched to cell mode. Read-only DataGrids still offer
  context copy and a filtered/sorted table snapshot.
- Mutable command/write/setup grids are never registered: context-menu
  clipboard actions must not interfere with edit controls or send IEC writes.

## Clipboard data
- Use the actual WPF `DataGridColumn.OnCopyingCellClipboardContent` authority.
  Template columns that present engineering values must set
  `ClipboardContentBinding`. No visual-tree traversal or hidden row realization
  for copying; display order follows current grid Items view and DisplayIndex.
- Copy table contains **only current filtered/sorted items and visible columns**.
  Selected blocks preserve row/column order and gaps. Use tabs and CRLF; quote
  embedded delimiters, line breaks, and quotes. Guard formula-like text when
  pasting into spreadsheet software, preserving signed numeric values.
- An explicit copy refuses, without changing the current Windows clipboard,
  selections above 3,000 rows, 36,000 cells, or 512 KB. Offer existing CSV
  export for larger data. No silent truncation.
- Clipboard failures are user actions only; no retries, hidden timers,
  background clipboard threads or telemetry-side work.
- Right-click must not change a grid's selected engineering object. The
  clicked cell/value is separate from selected block. IED Browser read/write
  and drill-down still depend on their original selection contract.

## IEC 61850 Owner provenance
- `C0100149` is four raw MMS Owner octets, decoding to IPv4 `192.16.1.73`.
- On SG7_BCU, the same IPv4 is also the IED endpoint. In that case the UI
  tooltip explicitly warns that it does **not** identify a particular MMS
  client/application/process. The original Owner bytes remain copyable.
- Never infer a client name from the Owner octets, cached report traffic,
  ARSAS session IP, SCL, or adjacent RCB. No network/RCB mutation.

## Validation contract
- Unit tests: TSV escaped values, Excel-selected gaps, ordering, multiline
  text, spreadsheet injection guards, null values, sizes and no partial copy.
- Windows build/XAML compile and full application regression.
- Manual: multiple cells via Shift/drag, Ctrl+C into Excel/Notepad,
  right-click cell, selected rows, filtered table; check EventLogs and IED
  Browser action selection, RCB Owner tooltip and copy.
- Physical MMS client/Owner attribution requires IED-side association proof.
