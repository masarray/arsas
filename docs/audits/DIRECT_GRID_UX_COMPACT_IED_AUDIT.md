# Direct grid UX and compact IED card audit

## Correct screenshot findings

The corrected ArIED screenshots show the application itself, and they confirm that the visible executable still has the PR #4 presentation:

- Control model is resolved (`SBO`, `Status only`).
- The Command Panel still shows the `Details` button and the `ControlLastResult` text line.
- Global Live Monitor still has a one-line column header with no filter inputs.
- DataGrid selection is still drawn as a rounded row card.
- The IED card still displays identity summary, scanned/selected/live counts, acquisition mode, and a separate action strip.

Therefore the previous answer blaming another application was incorrect. The screenshots are valid evidence from ArIED.

## Why the PR #5 approach was too fragile

PR #5 installed generic class handlers and waited for individual DataGrid Loaded events. Hidden TabItem content and virtualized row templates are not guaranteed to be materialized when those handlers run. Its Global Live Monitor filter also depended on finding the grid at exactly the right lifecycle point and inserting a separate sibling host.

Even when the code is present, this is less reliable than configuring the actual MainWindow visual tree, column headers, row generators, and IED item containers.

## Corrected architecture

`GridUxBehavior` now hooks the loaded MainWindow and retries only until the three concrete workspaces are found:

1. IED Explorer ListBox
2. IEC Command Panel DataGrid
3. Global Live Monitor DataGrid

After discovery it attaches directly to item/row generators, so virtualization and later-created TabItem content are handled.

## Compact IED card

The normal card is reduced to a dense single-row workspace item:

- technical IED icon with connection/report status dots;
- IED name;
- endpoint IP address and MMS port only;
- Play, Stop, Edit, and Remove icon buttons aligned on the right.

Removed from the visible card:

- logical-device summary;
- scanned/selected/live counters;
- acquisition/reporting sentence;
- unread-event badge;
- rounded action tray.

The existing discovery progress overlay remains available only while the individual IED is busy.

## Command Panel

- Remove `Details` and `Technical details` from realized and newly virtualized rows.
- Status-only/unavailable operation is represented by one plain `Not available` label.
- Hide the complete `ControlLastResult` line below Open/Close and other action buttons.
- Return rows to a compact 44 px operating height.

## Global Live Monitor

The filter inputs are now built directly into each actual DataGrid column header, rather than inserted as a separate sibling. This guarantees alignment with resizing and horizontal scrolling.

Columns:

- IED
- Signal
- IEC Telegram
- Value
- Quality
- IED Timestamp
- Acquisition

Filtering remains case-insensitive, token-based AND matching with 160 ms debounce, Escape clear, and Enter apply.

## Flat engineering grids

Every realized DataGrid row sets the shared `RowBorder` to zero corner radius and zero outer margin. The selected or recently changed highlight therefore fills a conventional rectangular engineering-grid row.

## Scope

- UI/layout/filtering only.
- No IEC 61850 control-sequence change.
- No RCB, MMS polling, event generation, or automatic command change.
- Filtering does not stop acquisition; it changes only the visible Global Live Monitor view.
