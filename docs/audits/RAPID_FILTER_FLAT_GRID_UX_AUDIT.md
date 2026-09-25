# Rapid filter and flat-grid UX audit

## Field findings

The latest OLSF/Siemens-style live test showed that the control behavior is now usable, but dense commissioning workflows still contain avoidable visual and navigation friction:

- Selection Wizard column filters keep showing `Filter…` while the field has keyboard focus.
- The IEC Command Panel still exposes Details/Technical details affordances that are no longer part of the fast control workflow.
- Command result/status-only explanations repeat information already visible in the Value and Control model columns.
- DataGrid selection is rendered as rounded cards instead of a conventional full-width engineering-grid highlight.
- Global Live Monitor has no rapid per-column filtering, which does not scale to thousands of signals.

## Implemented behavior

### Selection Wizard

- A column filter watermark is hidden immediately when the user clicks or tabs into the filter field.
- The watermark returns only after the empty field loses focus.
- Existing 180 ms filtering, Escape clear, Enter/Down navigation, sorting, and bulk selection behavior remain unchanged.

### IEC Command Panel

- `Details` is removed from every command row.
- `Technical details` is removed from generic/status-only rows.
- Status-only and otherwise unavailable controls show one plain `Not available` label.
- The `ControlLastResult` line below Open/Close and other actions is hidden.
- Rows return to a compact single-line operating height.

### Flat DataGrid highlight

- The shared `DataGridRow` template is normalized at load time:
  - no rounded radius;
  - no card-like row margin;
  - selected/changed backgrounds fill the normal rectangular row area.
- This applies consistently to Explorer, Command Panel, Global Live Monitor, Event Log, Diagnostics, and Selection Wizard grids.

### Global Live Monitor rapid filters

A per-column filter row is inserted directly below the column header for:

- IED
- Signal
- IEC Telegram
- Value
- Quality
- IED Timestamp
- Acquisition

Behavior:

- filtering is case-insensitive;
- multiple whitespace-separated terms use AND matching;
- refresh is debounced by 160 ms;
- Escape clears the active field;
- Enter applies immediately and returns focus to the grid;
- the filter row tracks column resizing and horizontal scrolling;
- filtering uses an `ICollectionView` over the existing virtualized `GlobalPoints` collection, avoiding a duplicate list of thousands of signals.

## Boundaries

- No change to IEC 61850 acquisition, RCB handling, polling, control execution, or event generation.
- No automatic commands or retries.
- Filtering affects only what is displayed in Global Live Monitor; the underlying monitored points and event stream remain active.
