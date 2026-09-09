# COMTRADE Viewer integration

## Product direction

ARSAS is the operator-facing product. ArdIrec supplies the COMTRADE parsing and analysis engine. Users should not need to locate a downloaded record manually or install/open a second product themselves.

The operator workflow remains stable across implementation phases:

`IED -> Fault Records -> Download -> Downloaded -> Open -> COMTRADE analysis`

## P0 — compatibility viewer

P0 intentionally kept the proven IEC 61850 file-transfer path unchanged and integrated the existing ArdIrec Qt desktop application as an internal compatibility viewer.

ARSAS responsibilities:

1. Use `FaultRecordRow.LocalDirectory` as the local record authority.
2. Show **Open** only for rows whose local state is `Downloaded`.
3. Require a local `.cfg` and same-stem `.dat` companion before opening.
4. Launch the compatibility viewer with `--arsas-open <absolute cfg path>` using `ProcessStartInfo.ArgumentList`.
5. Report invalid local packages without changing their transfer state.

ArdIrec compatibility responsibilities:

1. Keep standalone `ardirec.exe <record.cfg>` and `ardirec.exe --open <record.cfg>` compatibility.
2. Accept `--arsas-open <record.cfg>`.
3. Preserve Unicode paths through Qt application arguments.
4. Present hosted mode as **ARSAS — COMTRADE Viewer**.
5. Keep parsing/analysis on the existing ArdIrec code path.

P0 is proven and remains available as a fallback during P1 parity work.

## P1 — in-process native engine

P1 removes the normal process boundary. The preferred architecture is:

```text
ARSAS.exe
  WPF / XAML shell
       |
       | managed calls
       v
  ArdIrecNativeBridge.cs
       |
       | stable C ABI v1
       v
  Tools/ArdIrec/ardirec_bridge.dll
       |
       v
  ardirec_core
  ConfigParser / DatReader / analysis primitives
```

There is no second parser in C#. `ardirec_bridge.dll` is a small ABI adapter over the same `ardirec_core` used by ArdIrec. The managed side owns an opaque record handle and requests metadata or channel data through bulk-copy calls.

### P1A scope

P1A establishes the native boundary and the first ARSAS-native WPF workspace:

- ABI versioning and opaque native record lifetime.
- UTF-8 CFG open and existing ArdIrec bundle discovery.
- Record metadata and analog/digital channel metadata.
- Analog samples, digital states and raw timestamps.
- COMTRADE `TIMEMULT` applied when presenting the time axis.
- Native ARSAS `COMTRADE Workspace` window.
- Signal list and lightweight WPF analog/digital rendering.
- Native record decode performed off the WPF UI thread.
- Native-first Fault Records **Open** action.
- Automatic fallback to the proven P0 Qt viewer when the native bridge is absent or cannot open a record.
- Explicit **Full analysis** action inside the native workspace so field users can access the existing complete analysis surface until P1B/P1C reach parity.

For initial field validation, the WPF workspace limits one selected-channel preview to the first 500,000 frames. This is an explicit P1A boundary rather than a parser limitation.

### P1B target

P1B should add workstation interaction parity without changing the bridge ownership model:

- stacked multi-track waveform layout,
- shared time axis,
- trigger reference,
- cursor A/B measurements,
- zoom and pan,
- efficient full-record range/decimation access for large records,
- digital transition navigation.

### P1C target

P1C closes analysis parity before the Qt fallback can be retired:

- phasor view,
- harmonics,
- table/instant/RMS views,
- remaining ArdIrec analysis surfaces required by field workflows,
- final field comparison against the compatibility viewer.

## Runtime discovery

P1 native bridge discovery order:

1. `ARSAS_ARDIREC_BRIDGE_PATH`
2. `Tools/ArdIrec/ardirec_bridge.dll` beside ARSAS
3. `ArdIrec/ardirec_bridge.dll` beside ARSAS
4. `ardirec_bridge.dll` beside ARSAS

P0 compatibility viewer discovery remains available through `ARSAS_ARDIREC_PATH`, `ARDIREC_VIEWER_PATH`, and the staged `Tools/ArdIrec/ardirec.exe` layout.

The complete installer/folder layout during P1 parity is:

```text
ARSAS/
  ARSAS.exe
  Tools/
    ArdIrec/
      ardirec_bridge.dll   # preferred P1 path
      ardirec.exe          # P0 compatibility fallback / Full analysis
      Qt6*.dll
      qml/
      plugins/
      ...windeployqt runtime...
```

The legacy portable single-EXE remains a separate compatibility artifact and does not claim to embed the native/Qt runtime tree. The installer/folder distribution is the complete P1 field-validation package.

## Reproducible integration

The ARSAS pipeline must never resolve an unpinned "latest" ArdIrec build.

Current P1 contract:

1. The ArdIrec native C ABI bridge is merged to `masarray/ardirec` `main`.
2. `engines/ARDIREC.lock.json` uses schema 2 and pins an exact merged `main` commit.
3. The lock requires bridge ABI `1` at `Tools/ArdIrec/ardirec_bridge.dll`.
4. Qt remains pinned to 6.8.3 / `win64_msvc2022_64` for the compatibility fallback.
5. `scripts/stage-ardirec-viewer.ps1` builds ArdIrec core, bridge and desktop targets, runs ArdIrec regression/bridge tests, stages `ardirec_bridge.dll`, then deploys the Qt fallback runtime.
6. The dedicated COMTRADE integration workflow verifies both the native DLL and P0 fallback runtime from the same immutable ArdIrec revision.
7. The Windows installer workflow executes the ARSAS managed wrapper against a real binary CFG/DAT fixture from a Unicode path containing spaces.
8. `scripts/build-windows-installer.ps1` independently validates lock schema 2 / bridge ABI 1, requires the staged bridge and compatibility runtime, and executes the managed -> C ABI -> `ardirec_core` smoke against the ARSAS-owned release fixture before Inno Setup can produce an installer.
9. Installer smoke validation requires `ardirec_bridge.dll` as an installed product component.
10. The production release workflow inherits the same packaging guard because it stages the pinned ArdIrec revision and invokes the guarded installer builder. Release provenance records the exact ArdIrec repository/commit used to build both native and compatibility paths.

## P1A acceptance criteria

P1A is ready for field testing when:

- existing IEC 61850 Fault Records scan/download/re-download behavior is unchanged,
- **Open** still owns its click gesture independently of download selection,
- the pinned ArdIrec bridge builds and passes its native smoke tests on Windows,
- ARSAS can load the pinned bridge with ABI 1,
- a real binary CFG/DAT fixture is opened through .NET -> C ABI -> `ardirec_core`,
- metadata, analog samples, digital states and timestamps cross the ABI correctly,
- COMTRADE time presentation respects `TIMEMULT`,
- clicking **Open** normally creates **ARSAS — COMTRADE Workspace** without creating the Qt viewer process,
- native record open and selected-channel loading do not block the WPF UI thread,
- **Full analysis** can open the same record in the complete compatibility analysis surface while native parity is unfinished,
- missing/incompatible bridge conditions fall back to P0 rather than breaking the field workflow,
- the installer contains both native bridge and compatibility runtime,
- the installer builder refuses a package with an invalid P1 lock or missing native bridge,
- Build ARSAS, COMTRADE integration, SV regression and Windows installer validation are green on the final P1A head.
