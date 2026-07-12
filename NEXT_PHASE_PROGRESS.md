# ArIED 61850 v1.6.5 — Signal Selection Grid Phase B

## Scope

Phase B completes the behavior layer of the redesigned Signal Selection grid while preserving the compact Phase A layout.

## Filter-row legibility

- Column-header height increased from 61 px to 74 px.
- Filter row increased to 40 px.
- Filter editor increased to 36 px with vertically centered content.
- Horizontal-only padding avoids clipping the text line at common Windows DPI settings.
- Focus underline remains visible without reducing the text viewport.

## Sorting

- Every engineering column now has a live sort indicator.
- First click sorts ascending.
- Second click sorts descending.
- Third click restores the default engineering order: priority, Logical Node, then signal name.
- Sorting does not remove active filters or signal selections.

## Fast selection behavior

- Clicking a data row toggles its Use checkbox and keeps keyboard focus on that row.
- The Use header checkbox remains tri-state and applies only to currently visible rows.
- An indeterminate Use header selects all visible rows on click.
- `Ctrl+A` selects all visible rows while the grid is focused.
- `Ctrl+Shift+A` deselects all visible rows.
- `Space` toggles the focused signal.

## Keyboard filtering

- `Ctrl+F` focuses and selects the global search text.
- `Enter` or `Down` from a search/filter field applies the pending debounce immediately and moves focus to the first visible signal.
- `Escape` clears the focused search/filter field.
- Filter debounce remains 180 ms for responsive searching across large IED models.

## Efficiency

- Visible-row count, selected-visible count, and tri-state header status are calculated in one pass.
- The header checkbox is bound directly to view state rather than found through repeated visual-tree scans.
- Punctuation-only input is no longer counted as an active filter.
- DataGrid row and column virtualization remain enabled.

## Validation performed

- All XAML and project XML files parse successfully.
- All Signal Selection event handlers resolve to code-behind methods.
- 52 C# files passed structural delimiter validation.
- Old 61/29 px filter-header dimensions are absent from the wizard.
- Package contains no ARIEC61850 source and does not modify the user's engine repository.

A full WPF compile and live high-DPI visual test still require the user's Windows/.NET environment.
