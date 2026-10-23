# ARSAS ZIV AVR selected-control inventory + per-IED GOOSE capability (2026-10-20)

## Frozen approved UI source

User explicitly approved integrated blue selection accent from UI Phase 3. Frozen checkpoint `checkpoint/arsas-phase3-bluebar-accepted-20261020` at `109b1f93eea0ddd47858c6e0ba6fd6ed9d54bb6f`, exact-source Windows CI run `39428581363` built commit `36568539605b4f07c2b9ced72fce4f504f6737f5` (identical source tree), all 10 PR workflows successful, 2,419 ARSAS + 24 engine + 6 packaged regressions, native and portable smoke pass. Do not force-update this checkpoint. This independent regression branch stacks on it.

## Field evidence

Attached ARSAS diagnostic `1.6.40+36568539`, 2026-10-20 20:49 WIB: two simultaneously connected/monitoring IEDs. ZIVAVR live discovery recorded **GoCB=3**, SIEBCU **GoCB=1**; the SCL and live models are device-specific.

ZIVAVR has 5 selected CO DataObject roots:
- `ZIVAVRLD1/ATCC1.TapChg` (advisory SPC/BSC, live `ctlModel=0`)
- `ZIVAVRLD1/CSWI1.Pos` through `CSWI4.Pos` (DPC, live `ctlModel=0`).

The exact live MMS read proof reports `ctlModel=0 (StatusOnly)` for all five. This **does not authorize SBOw/Operate**. A separate operator-initiated `ControlModelWriteWindow` already allows a conditional, exact single CF ctlModel write and mandatory readback. A verified post-write reinspection may *then* authorize an action if typed CDC, ctlVal, status, and semantic proof all succeed.

## Root causes and implementation

1. `Iec61850MonitorDevice.RefreshCommandSignals()` incorrectly used `DistinctOperable(Signals)` for **row membership** instead of `DistinctSelected(Signals)`. A valid control with ctlModel=0 vanished, making its deliberate commissioning workflow inaccessible. Now exact selected CO roots stay present with unchanged SignalDefinition identity; only live verified operability gates any write. In-place collection reconciliation remains; status-only changes never reset the whole grid.
2. Event-driven `ConfigureCommandPanelButton` applied `ControlSupportsOperate` to all buttons except Open/Close, silently overriding the per-session IsConnected binding on **Model**. Exclude Model from generic write gate; the Model dialog retains explicit live association checks and engine-mediated exact CF read/write/readback. No automatic ctlModel writes, retries, or bypasses.
3. `FaultRecordUxBehavior` treated missing GoCB as `ProbeReady` (enabled). It now requires `device.HasGooseControlBlock` (available or unavailable, never speculative probe). The per-IED read-only computed capability uses current live GoCB directory for connected devices or own SCL-declared directory offline; live empty overrides stale SCL. It notifies on connection/discovery/SCL changes. The legacy GoCB shortcut uses the *same* property with WPF binding and click guard, plus shared entry-point guard. Global GOOSE Sniff remains a separate manual raw capture feature.

## Release / physical acceptance

- Positive: five ZIV selected CO rows visible as StatusOnly; Model available on connected session; conditional CF configuration only after operator action; no Open/Close/SBO until successful exact readback + typed reinspection.
- Negative: live ctlModel=0, unknown, inspection pending, lost association, unsafe typed CDC/status -> all physical controls disabled. No MMS write created by tab switch, selection, card projection or GOOSE discovery.
- GOOSE positive: connected SIEBCU GoCB=1 and ZIVAVR GoCB=3 active; negative: connected 0 GoCB disabled even if saved SCL claims otherwise; no cross-IED leakage, reconnect updates enablement. GOOSE capture alone is not GoCB proof.
- Frozen visual: integrated blue rail, Command Dock white icons, one fixed under-IP progress slot. No UI XAML or ARIEC engine modified.
- Exact-head Windows CI + native bridge/package smoke then real relay FAT. Do not merge to main or claim real control safety without operator-isolated equipment test.
