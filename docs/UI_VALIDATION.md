# ArIED 61850 UI Validation

Use this checklist for changes to the main window, signal-selection wizard, command panel, dialogs, or shared WPF styles.

## Test environment record

Document:

- Windows version;
- display resolution;
- Windows scaling percentage;
- light/dark OS preference when it affects system dialogs;
- .NET runtime and SDK version;
- keyboard-only and mouse workflow results;
- whether validation used synthetic, simulator, or live IED data.

## Main window

- [ ] The application opens at the minimum supported size without clipped primary actions.
- [ ] The title, product identity, active workflow, and session summary remain readable.
- [ ] Explorer, Live Monitor, Event Log, and Diagnostics navigation states are visually distinct.
- [ ] Keyboard focus is visible on navigation and actionable controls.
- [ ] The IED list scrolls independently from fixed project actions.
- [ ] Add IED, Open SCL, Connect All, Open Project, and Save Project remain discoverable.
- [ ] Long IED names, endpoint labels, status text, and diagnostic summaries trim or wrap without overlapping controls.

## Multi-IED behavior

- [ ] Selecting another IED changes only the detail workspace.
- [ ] Connecting, monitoring, stopping, or removing one IED does not change another IED unexpectedly.
- [ ] Connection, monitoring, unread-event, and diagnostic-alert indicators are distinguishable without relying only on color.
- [ ] A busy device does not block navigation or monitoring for another device.

## Signal-selection wizard

- [ ] Search and column filters remain legible at 100%, 125%, 150%, and 200% scaling.
- [ ] `Ctrl+F` focuses the global search field.
- [ ] `Enter` or `Down` moves from filtering to the first visible signal.
- [ ] `Escape` clears the active search or filter field.
- [ ] `Ctrl+A`, `Ctrl+Shift+A`, Space, and row-click selection behave as documented.
- [ ] The tri-state Use header applies only to visible rows.
- [ ] Sorting preserves filter and selection state.
- [ ] Row and column virtualization remain enabled.
- [ ] Cancel restores the previous selection.

## Live Monitor and Event Log

- [ ] Values, quality, source, and IED timestamp columns remain aligned.
- [ ] Recent-change highlighting does not obscure text or quality state.
- [ ] Event rows identify the originating IED and signal reference.
- [ ] Local PC time is not presented as the IED process timestamp.
- [ ] Large bursts remain responsive and do not create one dispatcher call per update.
- [ ] Copy and export actions produce readable, bounded output.

## Command panel

- [ ] Only the selected command row receives the active visual emphasis.
- [ ] Open, Close, Raise, Lower, Boolean, counter, and setpoint actions use the expected semantic label.
- [ ] Test, interlock, and synchrocheck flags remain on the same signal row.
- [ ] Open and Close require the staged confirmation step before dispatch.
- [ ] Confirm and Cancel actions are visually distinct and keyboard reachable.
- [ ] The current process value and detected control model are visible before dispatch.
- [ ] Long references, AddCause, LastApplError, and diagnostic evidence remain readable.
- [ ] A descriptor that is not operationally ready does not expose an enabled dispatch action.

## Accessibility and text behavior

- [ ] Text selection is disabled only where intentional and does not prevent copying diagnostics or references.
- [ ] Tooltips supplement rather than replace visible labels for critical actions.
- [ ] Color contrast remains readable on standard Windows displays.
- [ ] Focus order follows the visual workflow.
- [ ] Buttons have an accessible name even when represented by an icon.
- [ ] Reduced animation or disabled Windows animations does not hide state changes.

## Evidence

Attach only synthetic or sanitized screenshots. Do not include customer, employer, station, asset, credential, or restricted network information. Record the exact commit and scaling level with each screenshot set.
