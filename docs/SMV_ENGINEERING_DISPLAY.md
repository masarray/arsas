# SMV values and unit evidence — ARSAS engineering viewer

The SMV screen is deliberately read-only, sourced from the existing ARIEC61850/Npcap coherent stream. The graphics do not decode a second packet source.

## Value mode (SVScout-inspired, not a claim of OMICRON software compatibility)
- **Raw counts (default)**: plot the signed INT32 samples, RMS, fundamental and phasor magnitudes as received. Channel labels remain Ch1–Ch8. This is the correct fail-closed view for an unmapped IEC 61869-9/custom DataSet.
- **9-2LE · V/A (manual selection)**: available only when eight coherent, correctly indexed raw channels were decoded. Use the well-defined **9-2LE fixed channel order**, not the older ARSAS raw group labels:
  - Ch1–Ch4 → Ia, Ib, Ic, In: `A = count / 1000` (1 mA/count)
  - Ch5–Ch8 → Ua, Ub, Uc, Un: `V = count / 100` (10 mV/count)
- Selecting this mode is an **operator assumption that the publisher really implements the 9-2LE PhsMeas1 DataSet**. Eight channels alone do not prove semantic channel identity. UI/status must retain the user-profile warning; do not claim an arbitrary eight-channel stream is calibrated or standard-compliant.
- IEC 61869-9 custom DataSets, arbitrary channel ordering, CT/VT secondary display and SCL-specific sVC scales require separately verified typed DataSet metadata. These are **not inferred, guessed or automatically mapped** by this feature.
- Raw incoming bytes, continuity checks, original sampled counts and ARIEC61850 decoder are unchanged. Display vectors only are multiplied; angle stays mathematically identical.
- A mode change reprojects the current coherent snapshot and previously computed RMS/phasor rows, without recapture or extra packet ingestion.

## Visual QA and performance contract
- Waveform and phasor both use a **single, frozen, anti-aliased 2.7-DIP pen**; no glow, shadow or fake smoothing. Exact sample corners remain visible. Fine-step evidence must not be altered to look sinusoidal.
- Panel headings use independently aligned WPF Grid columns. Labels and legends do not overflow horizontally, and table values have right-aligned numeric columns and visible unit cells.
- Full per-packet rendering and background chart subscriptions are forbidden. Only bounded selected-stream snapshots reach the UI.
- The Smart Inno direct EXE is published only after Windows regressions and Program Files install/upgrade smoke pass. Still require human DPI screenshot and real merging-unit verification.

## Sources
- UCA IEC 61850-9-2LE scaling as documented by Beckhoff TwinCAT: https://infosys.beckhoff.com/content/1033/tf6510_tc3_iec61850/20981054731.html
- OMICRON SVScout product guide: https://www.omicronenergy.com/en/products/svscout/
- OMICRON user guide (time domain, primary units, cursors, phasor and tabular RMS): https://methodrep.com.br/wp-content/uploads/2022/09/SVScout-User-Manual-ENU.pdf
