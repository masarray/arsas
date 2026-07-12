# ArIED 61850

**Smart IED Explorer & Monitor**

ArIED 61850 is a Windows desktop engineering tool for IEC 61850 MMS discovery, multi-IED monitoring, reporting diagnostics, sequence-of-events viewing, project caching, and native IEC 61850 control through the ARIEC61850 Smart Control service.

## Core workflow

```text
Add or open IED
→ discover or restore cached model
→ select live and control objects
→ start independent per-IED monitoring
→ inspect reports, SOE, diagnostics, and commands
```

Each IED keeps its own connection, discovery progress, selected signals, report subscriptions, event indicator, and start/stop lifecycle.

## Main capabilities

- Live MMS discovery of Logical Devices, Logical Nodes, data objects, values, quality, and IED timestamps.
- Real IEDName and Logical Device boundary resolution.
- Multi-IED independent connections and monitoring.
- Static RCB/DataSet first, dynamic reporting when needed, and bounded MMS fallback for uncovered or unverified points.
- Saved project model cache for fast reconnect without repeating full discovery.
- Virtualized signal selection with search, filters, visible-row bulk selection, and persisted choices.
- Live value highlighting for recent process changes.
- SCADA-style event log focused on semantic process-state transitions.
- Per-IED unread event badges.
- Native Smart Control for command-ready IEC 61850 Data Objects.

## Native Smart Control

Version 1.6.5 integrates directly with `AR.Iec61850.Control.Iec61850ControlService` and adds a fast row-control workflow. The Command Panel shows the current process value and exposes semantic actions directly on each command row:

- Position/DPC: **Open** and **Close**
- Pulse controls: **Raise** or **Lower**
- Boolean/SPC: **True** and **False**
- Setpoint controls: value field and **Set**
- Optional **Details** window for the full ctlModel, sequence, checks, and protocol evidence


The command workflow reads the live IED model before enabling **Send Command**:

1. Validate the Data Object root.
2. Read `ctlModel`.
3. Retrieve the exact live `Oper`, `SBOw`, and optional `Cancel` type specifications.
4. Locate the named `ctlVal` field without positional guessing.
5. Select Direct Operate or SBO automatically.
6. Bind the requested value to the exact live MMS type.
7. Preserve origin, `ctlNum`, timestamp `T`, Test, interlock, and synchrocheck flags for the sequence.
8. Wait for CommandTermination for enhanced-security control models.
9. Show `ControlError`, `AddCause`, `LastApplError`, control number, control-service time, feedback time, total time, and process feedback.

For position objects such as `CSWI.Pos`, ArIED keeps the user-facing semantics **Open/Closed** even when a vendor encodes the wire `ctlVal` as Boolean. The feedback status is decoded independently as DPC/Dbpos. Control-object sessions are cached per IED association so opening the Command Panel and sending repeated commands does not repeat the full ctlModel/type discovery every time.

Supported typed command families include DPC, SPC, INC/ISC, BSC, and APC when the live descriptor is operationally valid.

ArIED intentionally has **no unsafe generic MMS-write fallback** for `.Oper`, `.SBOw`, or `.Cancel`.

## Engine requirement

ArIED is an app-only project. It references the user's existing ARIEC61850 source at build time and does not replace that repository.

This version was integrated against ARIEC61850 revision:

```text
41d003ae02b1003d16cd5a8baf5f7a95be4434fa
```

Required source includes:

```text
ARIEC61850/
└─ src/AR.Iec61850/
   ├─ AR.Iec61850.csproj
   └─ Control/
      ├─ Iec61850ControlService.cs
      ├─ Iec61850ControlObjectSession.cs
      ├─ Iec61850ControlModels.cs
      ├─ Iec61850ControlValueBinder.cs
      └─ Iec61850CommandTerminationDecoder.cs
```

The default folder layout is:

```text
D:\Git\
├─ ARIEC61850\
│  └─ src\AR.Iec61850\AR.Iec61850.csproj
└─ ArIED61850\
   └─ ArIED61850Tester.csproj
```

A different engine checkout can be selected explicitly:

```powershell
dotnet build .\ArIED61850Tester.csproj -c Release `
  -p:ArIec61850Project="D:\Git\ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj"
```

Or set:

```powershell
$env:ARIEC61850_PROJECT = "D:\Git\ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj"
```

## Build requirements

- Windows 10 or Windows 11
- .NET 8 SDK
- ARIEC61850 Smart Control source
- Visual Studio 2022 with .NET desktop development, or the .NET CLI

Build:

```powershell
dotnet restore .\ArIED61850Tester.csproj
dotnet build .\ArIED61850Tester.csproj -c Release
```

Portable self-contained package:

```powershell
.\scripts\publish-windows-portable.ps1 `
  -Version 1.6.5 `
  -EngineProject "D:\Git\ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj"
```

Expected output:

```text
dist\ArIED61850-1.6.5-win-x64-portable.zip
```

## Control safety

IEC 61850 commands can operate primary equipment. Before using live commands:

- Test all four control models with the IED Simulator.
- Verify DPC/SPC/INC/BSC/APC type variants.
- Verify positive and negative CommandTermination.
- Test interlock and synchrocheck rejection with `AddCause`.
- Verify Test mode causes no process movement.
- Test selection timeout, Cancel, association loss, and competing-client ownership.
- Confirm process feedback mapping for breaker and tap-changer controls.
- Use relay test/maintenance mode before energised commissioning.

The application enables Send Command only when the native descriptor reports `IsOperationallyReady=true` and the selected value can be represented safely by the live `ctlVal` type.

## Additional documentation

- `ARIEC61850_SMART_CONTROL_INTEGRATION.md`
- `SMART_CONTROL_FEEDBACK_AUDIT.md`
- `ENGINE_COMPATIBILITY.md`
- `NEXT_PHASE_PROGRESS.md`
- `docs/ARCHITECTURE.md`
- `docs/VALIDATION_CHECKLIST.md`

## Validation status

Static source validation was completed in the packaging environment. A full Windows `.NET 8` build, simulator run, and live relay control test could not be executed there because the environment does not contain the .NET SDK or an IEC 61850 target.

## License

Apache-2.0. See `LICENSE` and `NOTICE`.

## Copy Diagnostic

The Diagnostics tab includes **Copy Diagnostic**. Use it after a failed connection and paste the generated report into the support conversation. The report includes the app and engine versions, active Windows network adapters, IED endpoints, a short TCP reachability probe, native association state, association/discovery evidence, and the recent communication journal.

A message such as `TCP_CONNECTION_REFUSED` means port 102 rejected the socket before IEC 61850 COTP/ACSE/MMS negotiation. In that case an association-profile label such as `BalancedApTitle` is not the root cause.
