# P1D.3 — responsive disturbance + harmonics workstation

P1D.3 starts the Harmonics workstation-parity slice and fixes the field-observed interaction bottleneck in Time Signals.

## Time Signals performance contract

- C1/C2 movement must not rebuild analog/digital waveform geometry.
- Heavy track rendering is cached; cursor lines are a lightweight overlay.
- Drag-pan uses a translated preview and commits one local viewport update on mouse-up.
- Visible digital edge snapping uses one precomputed sorted time index and binary search instead of rescanning every digital transition on each pointer move.
- Host navigation/cursor notifications are capped at about 30 Hz during drag; final placement is always emitted.
- Native exact edge snap still runs on final cursor placement through the existing ArdIrec bridge.

## Text-density contract

- Digital lanes are title-only inside the plot; normal-state/circuit metadata remains in the Signals browser and event table.
- Repeated analog min/max micro-labels are removed from each track.
- Digital transition lines are collapsed to unique screen pixels.
- Up/down transition glyphs are displayed only when the current view has enough horizontal space to read them.
- Time-axis tick count adapts to available plot width.

## Harmonics P1D.3 start

- Native ArdIrec harmonic calculation remains authoritative; no managed DSP is added.
- Harmonics keeps selected analog channel + global C1 as the analysis-reference contract in this first P1D.3 slice.
- The WPF presentation is upgraded to a workstation layout with Fundamental RMS, THD, dominant harmonic, Nyquist limit, adaptive spectrum labels, a 5% engineering guide, and a compact selected-harmonic detail strip.
- Harmonic labels adapt to available pixels so H1 / selected / dominant information remains readable without a wall of text.

## Field acceptance

Use the same protection record that exposed the issue and verify:

1. continuous C1/C2 drag feels immediate and no longer redraws all tracks;
2. left/right pan previews immediately and commits/reloads only when the gesture completes;
3. dense protection transitions remain visually legible;
4. sparse transitions still show useful direction arrows;
5. Harmonics remains synchronized to C1 and renders without overlapping order labels.
