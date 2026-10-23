# P1D.3 — responsive disturbance + shared investigation timeline

P1D.3 starts the Harmonics workstation-parity slice, fixes the field-observed Time Signals interaction bottleneck, and promotes the cursor/timeline into a persistent workstation shell shared by analysis views.

## Shared investigation shell

- The time ruler is no longer owned by the waveform canvas. It is a persistent shell above Time Signals, Phasor and Harmonics.
- Time Signals and Phasor share global C1/C2 investigation cursors and show Δt in the shell.
- Phasor can select C1 or C2 as its native one-cycle DFT reference without moving either cursor.
- Harmonics switches the same ruler into a dedicated single `H` cursor, matching the one-reference harmonic workflow used by engineering COMTRADE tools.
- Moving the H cursor does not overwrite C1/C2.
- Timeline ticks use engineering-friendly 1/2/2.5/5 steps anchored to the trigger, so an exact `0` tick is present whenever the trigger is visible.
- The trigger is presented as a neutral fixed reference; cursor colors remain reserved for movable investigation markers.

## Trigger / DAT time-origin correctness

CFG `StartTime` describes the first recorded sample, while DAT timestamps are elapsed sample timestamps multiplied by `TIMEMULT`. Legacy and third-party COMTRADE files can encode their first DAT sample at a non-zero timestamp (for example one sample period) instead of raw zero.

Comparing raw DAT milliseconds directly with `TriggerTime - StartTime` therefore shifts the displayed trigger by exactly that DAT origin. P1D.3 now detects the source-frame-zero DAT timestamp and translates the CFG trigger into the same raw-axis coordinate system before rendering. The waveform samples themselves are not shifted or resampled, so source-frame identity, native edge snap and ArdIrec analysis remain exact.

This specifically protects old COMTRADE/SIGRA-style records where sample 1 starts at +1 sample period.

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
- Duplicate inline cursor/navigation prose is removed from the workspace chrome; the persistent ruler is the timing readout authority.

## Harmonics P1D.3

- Native ArdIrec harmonic calculation remains authoritative; no managed DSP is added.
- Harmonics uses the dedicated global H cursor as its analysis reference.
- The WPF presentation emphasizes Fundamental RMS, THD, dominant harmonic, Nyquist limit, adaptive spectrum labels, a 5% engineering guide, and a compact selected-harmonic detail strip.
- Harmonic labels adapt to available pixels so H1 / selected / dominant information remains readable without a wall of text.

## Field acceptance

Use the same protection record that exposed the issue and verify:

1. the fixed trigger line overlays the same physical sample/event as SIGRA and the shared ruler reads `0` there;
2. C1/C2 remain visible in the shell while moving between Time Signals and Phasor;
3. continuous C1/C2 drag feels immediate and no longer redraws all tracks;
4. left/right pan previews immediately and commits/reloads only when the gesture completes;
5. dense protection transitions remain visually legible;
6. Harmonics shows only one H cursor and moving it updates native harmonic analysis without disturbing C1/C2.
