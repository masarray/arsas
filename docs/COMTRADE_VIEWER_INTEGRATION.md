# COMTRADE Viewer integration

## Product direction

ARSAS is the operator-facing product. ArdIrec supplies the COMTRADE parsing and analysis capability. Users should not need to locate a downloaded record manually or install/open a second product themselves.

The target workflow is:

`IED -> Fault Records -> Download -> Downloaded -> Open -> COMTRADE analysis`

## P0 boundary

P0 intentionally keeps the proven IEC 61850 file-transfer path unchanged.

ARSAS responsibilities:

1. Use the existing `FaultRecordRow.LocalDirectory` as the local record authority.
2. Show a compact **Open** action only for rows whose local state is `Downloaded`.
3. Before launch, require a local `.cfg` and a same-stem `.dat` companion.
4. Launch the viewer with `--arsas-open <absolute cfg path>` using `ProcessStartInfo.ArgumentList`.
5. Report missing/invalid local packages without changing their transfer state.

ArdIrec responsibilities:

1. Keep standalone launch compatibility with `ardirec.exe <record.cfg>` and `ardirec.exe --open <record.cfg>`.
2. Accept `ardirec.exe --arsas-open <record.cfg>` for the ARSAS-hosted P0 workflow.
3. In ARSAS-hosted mode, present the window as **ARSAS — COMTRADE Viewer** instead of exposing a second product identity.
4. Route startup loading through the existing `DocumentController::openCfg()` path.
5. Keep CFG/DAT validation, parsing, waveform loading and analysis inside ArdIrec/`ardirec_core`.

## Runtime discovery

Development lookup order:

1. `ARSAS_ARDIREC_PATH`
2. `ARDIREC_VIEWER_PATH`
3. `Tools/ArdIrec/ardirec.exe` beside ARSAS
4. `ArdIrec/ardirec.exe` beside ARSAS
5. `ardirec.exe` beside ARSAS
6. Common build outputs from a sibling `ardirec` repository

The supported release layout is:

```text
ARSAS/
  ARSAS.exe
  Tools/
    ArdIrec/
      ardirec.exe
      Qt6*.dll
      qml/
      plugins/
      ...windeployqt runtime...
```

## Release reproducibility

The ARSAS release pipeline must not download an unpinned "latest" ArdIrec build.

Before P0 is marked release-ready:

1. Merge and validate the ArdIrec CLI-open seam.
2. Pin an exact ArdIrec commit in `engines/ARDIREC.lock.json`.
3. Build that commit with the same Windows recipe as ArdIrec: Qt 6.8.3 / MSVC 2022, Release configuration, then `windeployqt --release --compiler-runtime --no-translations --qmldir apps/desktop/qml`.
4. Use `scripts/stage-ardirec-viewer.ps1` to stage the deployed runtime under `Tools/ArdIrec/` in the ARSAS publish directory before Inno Setup runs.
5. Extend installer smoke tests to verify `Tools/ArdIrec/ardirec.exe` and its deployed Qt runtime exist.
6. Add an integration smoke fixture that launches a known-good CFG/DAT pair through the same ARSAS launcher contract.

## P0 acceptance criteria

P0 is complete when all of the following are true:

- Existing Fault Records scan/download/re-download tests remain green.
- A complete downloaded COMTRADE row exposes **Open**.
- A non-downloaded or partial row does not expose an actionable **Open** control.
- Missing CFG is rejected locally.
- CFG without same-stem DAT is rejected locally.
- Paths containing spaces launch correctly.
- ArdIrec opens the supplied CFG automatically and displays the loaded record.
- ARSAS-hosted launch is branded **ARSAS — COMTRADE Viewer**.
- ARSAS installer includes the pinned ArdIrec runtime under `Tools/ArdIrec/`.
- Installer smoke testing verifies the viewer component is present.

## P1 direction

P1 may remove the process boundary by exposing `ardirec_core` through a native bridge and rendering an ARSAS-native WPF COMTRADE workspace. The P0 operator workflow and local-package validation contract should remain stable so the UI does not need another redesign.
