# ARSAS Phase 3 — integrated IED Card selection accent (2026-10-20)

## Baseline / checkpoint

- **Phase 2 green reference**: `checkpoint/arsas-phase2-ui-green-20261020` at exact SHA `f0831fa0a92f95340f5c03f4186f9412b6c5e7ff` (GitHub Actions Build ARSAS run `39424944567`, 2,415 consumer + 24 pinned engine + 6 packaged tests passed; portable EXE smoke passed; user visual acceptance screenshot).
- **Original command/leaf FAT preserved**: `checkpoint/arsas-fat-green-20261020` (`30a7f18e...`) and field reference `checkpoint/arsas-field-command-stable-20261020` (`de71a1a4...`) are unchanged.
- Phase 3 is stacked on Phase 2, **not** a new mainline merge. Source branch: `ui/ied-card-integrated-selection-phase3-20261020`. No protocol, wire, command, Browser, persistence, discovery, or runtime behavior changes.

## Source-proven cause

The user screenshot showed a detached narrow blue line to the left of the selected IED Card. The actual production WPF style is **not** the dormant inline ListBoxItem style in `MainWindow.xaml`. Startup runs `P2BlueSteelGreigeUx.ApplyMainWindow` and replaces the ListBox's item style with `P2MainIedListItemStyle` from `Resources/P2BlueSteelGreige.xaml`. That theme template drew two sibling borders: `SelectionRail` at x=0, and the `Card` with `Margin=5,0,0,0`. The five-pixel empty gutter physically separated the accent from the rounded card surface. Modifying only the inline style would not fix the displayed program.

## Phase 3 as-built

- Keep exactly one physical IED Card (same single `ContentPresenter`, selection, focus, hover, live/offline brushes, existing command and monitoring ownership).
- Move selection rail to a **non-hit-testable overlay on the card's left border** and remove the detached 5px margin; the accent is 3px wide, flush at x=0, inset 8px vertically to respect the rounded 10px card corners. Layer order keeps the rail visible without a second card, extra layout column or hitbox.
- Preserve opacity/background=transparent when unselected, blue accent only on actual selected state (including combined selected+monitoring).
- Align both Explorer and independent FAT IED Card with the same geometry; retain FAT's own padding and state semantics.
- Set `SnapsToDevicePixels` and `UseLayoutRounding` to avoid subpixel hairline jitter at 100%, 125%, and 150% DPI.
- **Retain** the Phase 2 fixed 7px slot and single 3px progress below IED IP; no tooltips, state, buttons, progress timers, selection APIs, or data grids modified.

## Evidence & release gate

- New `P109ZIntegratedIedSelectionCardRegressionTests` checks both runtime theme templates, exact geometry and layer ordering, no margin/gap or hit-test interception, same selected/connected border brushes and accent triggers, active P2 runtime adapter, Stop/Play/Remove handlers and under-IP progress slot.
- Windows WPF build, full ARSAS/engine regression, native bridge build/CTest, portable EXE smoke and exact-head artifact must pass before calling a candidate ready to install.
- **Physical visual acceptance:** at 100%/125%/150% DPI check selected and unselected IED Cards, connected/monitoring, hover, discovery, Browser Read, card action buttons, FAT IED list. Blue accent must appear visually integrated in the straight part of the card's left rounded edge; there must be no separate blue column/gap and no card bounce. Command Dock row/commands must remain unaffected.
- Do not merge/release to public `main` on CI alone; physical review and operator approval remain required.
