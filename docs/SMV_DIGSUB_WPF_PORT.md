# ARSAS SMV — DigSubAnalyzer WPF viewer port

Source of truth: [masarray/DigSubAnalyzer](https://github.com/masarray/DigSubAnalyzer), revision `cac3bef88ceaa335013248bbb5093aaa5a7b443a`.

Both ARSAS and DigSubAnalyzer use GPL-3.0-or-later. Ported source retains
its original `ProcessBus.*` namespace and is located under
`Ports/DigSubAnalyzer/`:

- `WaveformPanelControl` (original renderer, timebase, overlay/stack,
  two separated lanes, cursor, auto range and peak hold);
- `PhasorDiagramControl` with the original collision-aware callouts,
  independent family radii and reused rendering resources;
- their presentation-only models and phase label geometry helpers.

The ARSAS main SMV tab **uses the real ported controls**, not the old Canvas
renderer. Only the adapter `Views/SmvDigSubPresentationAdapter.cs` translates
immutable coherent samples and raw DFT vectors into these presentation models.
`ARIEC61850` and `SmvLiveCaptureService` remain the only protocol capture
and decoder pipeline; this port does **not** introduce a second decoder.

## Presentation contract

- Phase-align **screen samples only** by `smpCnt mod samplesPerCycle` across
  whole-cycle coherent windows; never mutate underlying capture evidence.
- Set phasor angular reference to the first valid raw channel (typically Ch1)
  and subtract common ring-buffer DFT rotation; **reference Ch1 = 0°**.
  Measurement table and phasor plot use the same normalized vectors.
- RAW Ch1–4 (A) and Ch5–8 (B) are **not asserted to be U/I**.
  A real IED SCL/ordered Dataset mapping and scale factors must be verified
  before showing calibrated V, A, power, or physical phase labels.
- Frozen/held snapshots remain visually distinguished from live acquisition.
- Decode and capture per-NIC stream identity, continuity, and limits remain
  governed by the existing ARIEC61850-backed bounded capture runtime.

## Not a field qualification

The code-level integration and CI regression protect layout contracts,
counter alignment and relative phasor math. Native Windows layout acceptance,
continuous high-rate physical MU ingress, actual ordered Dataset membership,
and field accuracy must be checked separately before labeling this production.
