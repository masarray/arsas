# ARSAS SMV Tab — Blue-White Measurements Visual Checkpoint
Date: 2026-10-22

## Purpose
Freeze the user-accepted ARSAS SMV layout and preserve its working live ARSTACK
stream behavior. This checkpoint changes ONLY the Measurements DataGrid palette
to match the light blue-white waveform panels. It is not an engine, scaling,
timebase, phasor, capture, or installer redesign.

## Preserved baseline
- PR #671 branch `feat/smv-sharp-latest-p0-20261022` retains the current P0
  IED Browser, native ARIEC61850 decoder, DigSub waveform/phasor rendering,
  ARSTACK real-SV coherent sample handling, selected NIC, timebase controls,
  stream discovery, and live continuity indicators.
- The layout remains a compact explorer + waveform + phasors/measurements
  three-column workstation. Measurements binds the existing eight-channel
  read-only `Measurements` collection and five fields: Channel, RMS,
  Fundamental, Angle, Unit. No change to the numeric calculations.
- Existing raw counts default and explicit 9-2LE engineering display profile
  remain unchanged. Nothing in this checkpoint auto-assumes channel meaning.

## Locked palette — Measurements only
- Main blank grid / normal rows: `#F8FBFF`, white with a cool blue tint.
- Alternating rows: `#EDF5FF`, soft blue.
- Header: `#E8F2FF`, matching the adjacent waveform section background.
- Header divider: `#D5E4F6`; cell/row separators: `#E2EBF7`.
- Row hover: `#E4F0FF`. Existing ARSAS selected-row blue stays intact.
- Ink: shared `Ink` resource; channel dots retain phase colors.
- Scoped styles extend the existing `ModernDataGrid` and implicit ARSAS
  DataGrid header/cell/row templates. Never recolor every ARSAS DataGrid.

## Regression and publication gate
- Parse `SmvInlineViewer.xaml` and check local grid, header, row and cell
  palette plus numeric data bindings in `SmvMeasurementPaletteRegressionTests`.
- Full Windows app tests, pinned engine tests, native bridge, and direct Smart
  Inno EXE Program Files install / in-place upgrade / uninstall must PASS.
- Only after passing, preserve the exact source SHA using a dedicated
  checkpoint branch and immutable direct EXE prerelease tag.
- Keep work as engineering preview until on-screen Windows DPI and physical
  merging-unit acceptance; do not overwrite stable.
