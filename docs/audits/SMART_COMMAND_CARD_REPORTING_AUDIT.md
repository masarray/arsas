# Smart command panel, relay card, and reporting recovery audit

## Field observations

The live OLSF/OCR7 test exposed five separate issues:

1. The IED card used an improvised generic device icon, while the requested protection-relay/BCU SVG was not used. Runtime visual-tree re-parenting also placed an overlay above the action strip, so Play, Stop, Edit, and Remove could become non-clickable.
2. The Command Panel changed its row structure, dimensions, and child visibility after the Expander had already painted, producing a visible first-open layout flicker.
3. A selected `XCBR.Pos.stVal` point remained on MMS polling while another position point was dynamically report-covered. When a static report candidate returned no exact selected-member mapping, the previous recovery condition required at least one statically covered point, so the all-uncovered case never queued dynamic recovery.
4. The shared DataGrid row template retained a rounded selection border and card-like margin.
5. Status-only, unresolved, feedback-only, and otherwise non-operable objects remained in the Command Panel and added noise.

## IED card correction

The card is now a stable XAML DataTemplate rather than a post-render visual-tree transformation.

- Uses the exact SVG path supplied by the user for a protection relay / BCU silhouette.
- Shows only the IED name and `IP:port` identity in normal state.
- Places Play, Stop, Edit, and Remove directly below the IP address.
- Keeps direct button click handlers and unobstructed hit targets.
- Preserves connection/report activity dots and the existing card-local busy/discovery overlay.
- Removes logical-device summary, scanned/selected/live counters, acquisition prose, and unread-event badge from the normal card.

## Command Panel correction

- Removed the Expander-open refresh path that changed data after the first frame.
- Removed post-render row-height, column-width, and child-visibility mutations.
- Fixed the operating grid to a stable 44 px row height.
- Removed helper prose, Technical details, Details, and result text below action buttons.
- The command projection now includes only selected objects whose live `ctlModel` is resolved and supports Operate.
- `StatusOnly`, unknown, generic fallback, unsupported, and feedback-only objects remain available in monitoring views but are excluded from the operating panel.
- Control-model inspection is preloaded independently of opening the Expander, and the projection is refreshed only after inspection completes.

## Dynamic-report recovery correction

Smart Auto still follows the safe order:

1. Confirmed static DataSet/RCB coverage.
2. Association-scoped dynamic DataSet/URCB recovery when enabled and required.
3. MMS polling only as the final fallback.

The all-uncovered static case is now handled. If a static report starts but returns zero exact selected-member references, every uncovered selected point is queued for dynamic recovery. Partial static coverage continues to queue only the uncovered remainder.

MMS polling can still remain when dynamic writes are disabled, no safe RCB is available, the IED rejects DataSet/RCB writes, or report identity cannot be proven safely. The change does not guess report mappings or project an ambiguous InformationReport onto an arbitrary DataSet.

## Flat engineering grids

The shared DataGrid row presenter now uses:

- `CornerRadius=0`
- `Margin=0`

Selection and recent-change highlights therefore fill the normal rectangular engineering row instead of appearing as rounded cards.

## Safety boundaries

- No automatic command retry.
- No second Select/SBOw or Operate.
- No change to `ctlNum`, origin, Test, Check, interlock, synchrocheck, or CommandTermination.
- No unsafe RCB/DataSet projection.
- MMS polling remains available when reporting cannot be established safely.
