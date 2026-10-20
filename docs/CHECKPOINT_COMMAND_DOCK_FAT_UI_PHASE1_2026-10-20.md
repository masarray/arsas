# ARSAS Command Dock FAT — frozen checkpoint and P0 UI Phase 1

**Date:** 2026-10-20 · **Owner scope:** Command Dock header cosmetics only.

## Frozen source checkpoints (do not force-update/rebase)

| Type | Branch | Full SHA | Evidence |
| --- | --- | --- | --- |
| Exact-SHA Windows FAT / native bridge | `checkpoint/arsas-fat-green-20261020` | `30a7f18e8e575604eb1306c96753daab9f9c4a51` | GitHub Actions `39404069355`; all 11 CI workflows succeeded; 2,409 consumer + 24 pinned engine + 6 packaged regression tests passed; portable EXE smoke test passed. |
| User-tested field software | `checkpoint/arsas-field-command-stable-20261020` | `de71a1a45c30d0221766c389d96553e051b20c17` | Screenshot and diagnostic at 2026-10-20 17:40 +07; Git source tree byte-for-byte identical to green FAT commit (compare shows no changed files); user reports stable grid and command improvement. |

The field SHA has the tested FAT SHA as a merge parent, but its **own separate CI run was not recorded**. Do not claim separate field-SHA CI or a complete relay FAT. Engine exact SHA: `c85d4e20db722bd0c2729f2533b323b13d3ef735`.

Field command diagnostic demonstrates *positive* SBOw, Operate, CommandTermination and confirmed process feedback for real commands. Example click-to-result ~414–461 ms, with a ~399–404 ms local post-SBOw-response wait; this is **not** interpreted as physical IED CommandTermination latency or on-wire RTT. Repeated already-at-state requests were reported `NotSent` (no control wire requests), as required.

## Phase 1 — strict UI-only changes

- Add exact filled SVG contours for chevron down/up and power as static WPF Geometry resources.
- Scope a transparent, white-glyph Expander header template **only** to `CommandPanelExpander`; one accessible keyboard/mouse ToggleButton controls original `IsExpanded` property and original ContentPresenter.
- Remove the colored power card, border and blue badge; render power as white glyph beside white `Command Dock` label.
- Keep dark header surface, target badge, DataGrid instances, persistent workspace lifetime, handlers, and all control authorization and wire code unchanged.
- Use template-triggered instant chevron reversal; no timers, animations, icon downloads, image files or extra commands.
- Source contract regression `P109XCommandDockWhiteGlyphRegressionTests`: exact SVG geometry, transparent scope, keyboard toggle, collapsed/expanded content, no command handler or data grid loss.

## Release / acceptance gates

1. Require exact-head Windows CI green, all packed/native smoke regression checks, and downloadable portable artifact for the icon PR.
2. Visual FAT on 100% and 150% DPI: glyphs white and aligned, no power bubble, chevron down when collapsed and up when expanded, full header target badge remains intact.
3. Confirm keyboard Space activation, navigation Explorer -> Browser -> Diagnostics -> Explorer, no flicker and no newly queued MMS work from expand/collapse.
4. Validate no negative change to previously proven command response stages. Do not send unnecessary commands to energized equipment.

**Deferred Phase 2 (out of scope):** redesign discovery progress to a fixed under-IP thin line rather than a separate empty animation card. Follow-up must reuse existing per-IED activity progress authority instead of creating another progress SSOT.

**Do not merge into main/release on CI alone.** This PR is an isolated visual trial based on a frozen field checkpoint.
