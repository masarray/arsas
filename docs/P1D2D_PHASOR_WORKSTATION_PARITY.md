# P1D.2D — Phasor workstation parity

P1D.2D ports ArdIrec's record-level Phasor workflow into the native ARSAS WPF COMTRADE workspace. Native ArdIrec remains the calculation authority; WPF only selects the investigation reference and renders the returned vectors.

## Operator contract

- Phasor is a record-level view. It does not require the currently highlighted signal row to be analog.
- Voltage and Current diagrams are visible at the same time.
- The two diagrams use independent radial scales. Volts and amps are never compared on one numeric scale.
- Both diagrams use one global investigation reference selected from C1 or C2.
- C1/C2 positions remain owned by Time Signals; switching the Phasor reference does not move either cursor.
- The reference strip reports the exact native source frame and trigger-relative time used for the DFT.
- Moving between Time Signals and Phasor does not reset C1/C2.

## Channel selection

ArdIrec P1D.2A analog role and phase semantics are authoritative when the capability is present.

For each electrical role, ARSAS selects one coherent phase family:

1. prefer the same circuit and same engineering unit with the largest distinct L1/L2/L3/E coverage;
2. if circuit metadata is sparse or phase-specific, fall back to the same engineering-unit family with the largest phase coverage;
3. keep the first source channel for duplicate phase identities;
4. order displayed vectors L1, L2, L3, E.

This prevents one normalized polar plot from silently mixing unlike units or unrelated bay circuits.

## Calculation boundary

- Fundamental RMS magnitude and angle come only from `ardirec_record_get_phasor`.
- ARSAS does not implement a second DFT, RMS, phase-angle or sample-window algorithm.
- P1D.2A Primary/Secondary representation capability is not surfaced here. The global representation/measurement UI remains the P1D.2B responsibility.
- Harmonics remains the existing selected-channel/C1 workflow until its own parity slice.
- Locus / distance and Engineering Table are outside P1D.2D.

## Acceptance

- Two simultaneous Voltage/Current panels on a record containing both roles.
- Phasor remains available after a digital row is selected.
- C1 and C2 can each become the active Phasor reference without changing cursor position.
- The exact source frame follows P1D.2C timestamp/source-frame identity on large and multi-rate records.
- Native role/phase semantics drive phase-family selection.
- Same-unit and coherent-circuit selection is deterministic and regression tested.
- Existing Time Signals, Harmonics, native bridge, installer and compatibility-viewer gates stay green.
