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
3. Read startup paths through Qt's Unicode-safe application argument API.
4. In ARSAS-hosted mode, present the window as **ARSAS — COMTRADE Viewer** instead of exposing a second product identity.
5. Route startup loading through the existing `DocumentController::openCfg()` path.
6. Keep CFG/DAT validation, parsing, waveform loading and analysis inside ArdIrec/`ardirec_core`.

## Runtime discovery

Development lookup order:

1. `ARSAS_ARDIREC_PATH`
2. `ARDIREC_VIEWER_PATH`
3. `Tools/ArdIrec/ardirec.exe` beside ARSAS
4. `ArdIrec/ardirec.exe` beside ARSAS
5. `ardirec.exe` beside ARSAS
6. Common build outputs from a sibling `ardirec` repository

The supported installer/folder release layout is:

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

The legacy portable single-EXE distribution remains a separate compatibility artifact in P0 and does not pretend to embed the Qt viewer runtime. The installer/folder distribution is the complete P0 COMTRADE Viewer experience.

## Reproducible integration

The ARSAS release pipeline must never resolve an unpinned "latest" ArdIrec build.

Current P0 contract:

1. The ArdIrec CLI/hosted-open seam is merged to `masarray/ardirec` `main`.
2. `engines/ARDIREC.lock.json` pins an exact merged `main` commit and requires `ref=main`.
3. Qt is pinned to 6.8.3 / `win64_msvc2022_64`.
4. `scripts/stage-ardirec-viewer.ps1` configures and builds ArdIrec Release, runs its regression tests, then performs `windeployqt --release --compiler-runtime --no-translations --qmldir apps/desktop/qml`.
5. The deployed runtime is staged under `Tools/ArdIrec/` before Inno Setup runs.
6. The dedicated integration workflow smoke-launches a known-good CFG/DAT pair from a Unicode path containing spaces using the same `--arsas-open` argument contract as ARSAS.
7. The Windows installer validation workflow requires the installed ArdIrec executable, core Qt DLLs, and `platforms/qwindows.dll`.
8. The production Windows release workflow uses the same pinned ArdIrec revision and staging script and records the COMTRADE Viewer repository/commit in release provenance.

## P0 acceptance criteria

P0 is complete when all of the following are true on the final ARSAS PR head:

- Existing Fault Records scan/download/re-download tests remain green.
- A complete downloaded COMTRADE row exposes **Open**.
- A non-downloaded or partial row does not expose an actionable **Open** control.
- Missing CFG is rejected locally.
- CFG without same-stem DAT is rejected locally.
- Paths containing spaces and Unicode characters launch correctly.
- ArdIrec opens the supplied CFG automatically and displays the loaded record.
- ARSAS-hosted launch is branded **ARSAS — COMTRADE Viewer**.
- ARSAS installer includes the pinned ArdIrec runtime under `Tools/ArdIrec/`.
- Installer smoke testing verifies the viewer component is present.
- ARSAS build/regression tests, COMTRADE cross-repo smoke, and installer validation are green against the merged pinned ArdIrec `main` revision.

## P1 direction

P1 may remove the process boundary by exposing `ardirec_core` through a native bridge and rendering an ARSAS-native WPF COMTRADE workspace. The P0 operator workflow and local-package validation contract should remain stable so the UI does not need another redesign.
