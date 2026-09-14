# COMTRADE Viewer integration

## Product direction

ARSAS is the operator-facing product. ArdIrec supplies the pinned COMTRADE parsing/analysis core through a small native C ABI bridge. Users do not install, locate, or launch a second viewer application.

The operator workflow is:

`IED -> Fault Records -> Download -> Downloaded -> Open -> ARSAS COMTRADE Workspace`

## Native-only architecture

COMTRADE **Open** is an in-process ARSAS workflow:

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
  ardirec_bridge.dll
       |
       v
  ardirec_core
  ConfigParser / DatReader / analysis primitives
```

There is no second COMTRADE parser in C# and there is no external ArdIrec process fallback. `ardirec_bridge.dll` is the ABI adapter over the same ArdIrec core. The managed side owns an opaque native record handle and requests metadata or channel data through bulk-copy calls.

The legacy Qt desktop launcher, `ardirec.exe` packaging, `ARSAS_ARDIREC_PATH`, `ARDIREC_VIEWER_PATH`, process activation code, and Qt runtime deployment are intentionally unsupported by ARSAS.

## Current native workspace scope

The native integration provides:

- ABI versioning and opaque native record lifetime,
- UTF-8 CFG open,
- CFG/DAT package resolution inside the downloaded fault-record directory,
- record metadata and analog/digital channel metadata,
- analog samples, digital states and raw timestamps,
- COMTRADE `TIMEMULT` aware time presentation,
- ARSAS-native `COMTRADE Workspace` window,
- native record decode off the WPF UI thread,
- bounded zoom/pan/reset viewport math,
- timestamp-based X positioning,
- trigger reference and Cursor A/B measurements,
- display-oriented decimation and transition-driven digital rendering.

Any analysis surface not yet implemented natively must be added to the ARSAS workspace; it must not reintroduce an external viewer process.

## Runtime discovery

The native bridge discovery order is:

1. `ARSAS_ARDIREC_BRIDGE_PATH` for explicit development/integration-test override,
2. `Tools/ArdIrec/ardirec_bridge.dll` beside ARSAS,
3. `ArdIrec/ardirec_bridge.dll` beside ARSAS,
4. `ardirec_bridge.dll` beside ARSAS.

Official folder/installer packages publish the bridge at:

```text
ARSAS/
  ARSAS.exe
  Tools/
    ArdIrec/
      ardirec_bridge.dll
```

The official portable package remains a real single EXE. The same pinned bridge is embedded as a managed resource and materialized into the user's local ARSAS native cache at startup when no physical bridge is present. The bootstrap then sets `ARSAS_ARDIREC_BRIDGE_PATH` for the normal bridge loader.

## Reproducible integration

The ARSAS pipeline never resolves an unpinned "latest" ArdIrec build.

Current contract:

1. `engines/ARDIREC.lock.json` pins an exact `masarray/ardirec` `main` commit.
2. The lock requires bridge ABI `1` at `Tools/ArdIrec/ardirec_bridge.dll`.
3. `scripts/build-ardirec-bridge.ps1` checks out/validates the pinned source, configures ArdIrec with desktop/Qt disabled, builds the core + C ABI bridge, and runs native regression tests.
4. `scripts/publish-windows-portable.ps1` requires the pinned bridge for official Windows packaging.
5. Folder/installer publish copies the physical bridge to `Tools/ArdIrec/ardirec_bridge.dll`.
6. Single-file publish embeds the bridge in `ARSAS.exe`; `ArdIrecEmbeddedBridgeBootstrap` materializes it for native loading.
7. The COMTRADE integration workflow rejects any reintroduction of `Process.Start`, `TryLaunch`, `ardirec.exe`, `ARSAS_ARDIREC_PATH`, or `ARDIREC_VIEWER_PATH` in the application launch path.
8. The installer workflow exercises the managed wrapper against a real CFG/DAT fixture from a Unicode path containing spaces.
9. Installer/release smoke validation requires `ardirec_bridge.dll` and explicitly rejects the legacy ArdIrec executable/Qt runtime from the installed product.
10. Release provenance records the exact ArdIrec repository, commit, ABI, and bridge location used by the package.

## Acceptance criteria

The native COMTRADE integration is acceptable when:

- existing IEC 61850 Fault Records scan/download/re-download behavior is unchanged,
- **Open** owns its click gesture independently of download selection,
- the pinned ArdIrec bridge builds and passes native tests on Windows,
- ARSAS loads ABI 1 and opens a real binary CFG/DAT fixture through `.NET -> C ABI -> ardirec_core`,
- metadata, analog samples, digital states and timestamps cross the ABI correctly,
- COMTRADE time presentation respects `TIMEMULT`,
- clicking **Open** creates the ARSAS native COMTRADE workspace without creating another viewer process,
- native record open and channel loading do not block the WPF UI thread,
- missing/incompatible bridge conditions fail clearly inside ARSAS rather than launching a fallback program,
- installer packages contain the native bridge and no ArdIrec desktop/Qt runtime,
- portable single-EXE packages contain the same pinned bridge as an embedded resource,
- Build ARSAS, COMTRADE integration, IO/FAT regression, and Windows installer validation are green on the final head.

## Historical note

Early P0/P1 development used an ArdIrec Qt compatibility viewer while the native workspace was being established. That process boundary is retired. Historical references to the Qt fallback are not part of the current product contract and must not be restored as a production fallback.
