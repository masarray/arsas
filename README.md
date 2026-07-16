<div align="center">
  <img src="Assets/app-icon.png" alt="ARSAS application icon" width="104" height="104" />

# ARSAS

### IEC 61850 IED Explorer, Multi-Device Monitor, GOOSE Subscriber & Smart Control Workstation

**A modern Windows engineering application for SCL-assisted workflows, live MMS model discovery, report-first monitoring, read-only GOOSE subscription, sequence-of-events analysis, diagnostics, and guarded IEC 61850 control.**

[![Build](https://github.com/masarray/ArIED61850Tester/actions/workflows/build.yml/badge.svg)](https://github.com/masarray/ArIED61850Tester/actions/workflows/build.yml)
[![Pages](https://github.com/masarray/ArIED61850Tester/actions/workflows/pages.yml/badge.svg)](https://masarray.github.io/ArIED61850Tester/)
[![License](https://img.shields.io/badge/license-GPL--3.0--or--later-2563eb)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-0ea5e9)](#requirements)

[**Product website**](https://masarray.github.io/ArIED61850Tester/) · [**Quick start**](#quick-start) · [**Architecture**](docs/ARCHITECTURE.md) · [**GOOSE Subscriber**](docs/GOOSE_SUBSCRIBER.md) · [**Validation**](docs/VALIDATION_CHECKLIST.md) · [**Report an issue**](https://github.com/masarray/ArIED61850Tester/issues)
</div>

![ARSAS engineering workspace](landing/assets/hero.svg)

## Built for practical IEC 61850 engineering

ARSAS brings the most common commissioning and troubleshooting workflows into one focused desktop workspace. Add an IED by IP address, import endpoints from an SCL file, verify the live MMS model, select the required signals, monitor each device independently, subscribe to station/process-bus GOOSE streams without transmitting, inspect report and event evidence, and stage supported control operations through the native ARIEC61850 control service.

The application is designed for **substation automation laboratories, FAT/SAT preparation, relay and BCU integration, commissioning support, protocol investigation, and repeatable engineering diagnostics**. It is not a formal conformance certificate and does not replace approved switching procedures, test plans, or site authority.

## Engineering capabilities

| Area | Current capability |
|---|---|
| **SCL workspace** | Import SCD, CID, ICD, IID, SSD, or XML files; extract configured endpoints; preserve multi-IED project context; support design-to-live verification workflows. |
| **MMS discovery** | Discover Logical Devices, Logical Nodes, Data Objects, Data Attributes, values, quality, timestamps, DataSets, RCBs, GOOSE control blocks, and available model metadata. |
| **Multi-IED sessions** | Maintain independent connection, discovery, monitoring, report, event, and lifecycle state for every configured IED. |
| **Report-first monitoring** | Prefer configured RCB/DataSet coverage, use temporary dynamic reporting where appropriate, and poll only still-uncovered points. |
| **Signal workspace** | Search, filter, sort, select visible rows, persist selections, and work efficiently with large IED models through WPF virtualization. |
| **Live monitoring** | Coalesced live values, recent-change highlighting, IED-provided timestamps, quality display, and bounded UI updates. |
| **GOOSE Subscriber** | Capture IEC 61850-8-1 GOOSE through the native ARIEC61850 Npcap transport; show APPID, MAC/VLAN, `goCBRef`, DataSet, `stNum`/`sqNum`, TAL and diagnostics; bind every ordered `allData` leaf to SCL or live-discovery DataSet metadata. |
| **Sequence of events** | Process-oriented event log, per-IED unread indicators, report reasons, and semantic state transitions. |
| **Smart Control** | Discover `ctlModel`, inspect live MMS types, execute supported Direct or Select-Before-Operate sequences, and surface termination/error evidence. |
| **Diagnostics** | Connection journal, association evidence, TCP reachability context, report and GOOSE diagnostics, command evidence, and copyable support reports. |
| **Project persistence** | Save device definitions, cached model context, selected signals, and operator workspace state for faster return visits. |

## Workflow

```text
Open SCL / Open Project / Add IED
                 ↓
Review configured endpoint and live connection state
                 ↓
Discover or restore the IEC 61850 model
                 ↓
Select signals and control-ready Data Objects
                 ↓
Start report/poll monitoring independently per IED
                 ↓
Optionally subscribe to GOOSE on an approved Npcap adapter
                 ↓
Inspect live values, ordered GOOSE leaves, reports, SOE,
diagnostics, and command evidence
```

Each device owns its own MMS session. GOOSE capture is a separate read-only Ethernet subscriber and does not implicitly connect, monitor, control, or reconfigure an IED.

## SCL-aware GOOSE subscription

The **GOOSE Subscriber** tab uses ARIEC61850's raw-Ethernet parser, Npcap frame source, and `ProcessBusStreamMonitor`. It never publishes GOOSE or writes a GSEControl object.

For each detected stream, ARSAS displays:

1. APPID, source/destination MAC, VLAN ID/priority, `goCBRef`, `goID`, DataSet reference, and `confRev`.
2. `stNum`, `sqNum`, TimeAllowedToLive, Test, `ndsCom`, packet count, retransmission/state-change classification, and sequence/TAL diagnostics.
3. Every `allData` item in exact wire order.
4. Signal name, object reference, FC, CDC/bType, previous value, changed state, and model-binding source.

Binding priority is **loaded SCL GOOSE/DataSet model → live MMS discovery GOOSE/DataSet model → unbound ordered leaf fallback**. Model/frame count mismatches remain visible instead of being silently truncated. See [GOOSE Subscriber engineering notes](docs/GOOSE_SUBSCRIBER.md).

## Smart Control, without protocol guessing

For supported control objects, ARSAS reads the live model before enabling command dispatch:

1. Validate the selected Data Object root.
2. Read `ctlModel` and determine the required control sequence.
3. Resolve the live `Oper`, `SBOw`, and optional `Cancel` type descriptions.
4. Locate and bind the named `ctlVal` field using the actual MMS type.
5. Preserve origin, `ctlNum`, timestamp `T`, Test, interlock, and synchrocheck values across the sequence.
6. Wait for `CommandTermination` when required by enhanced-security control models.
7. Present `ControlError`, `AddCause`, `LastApplError`, timing, and process-feedback evidence.

Supported command families currently include DPC, SPC, INC/ISC, BSC, and APC when the discovered live descriptor is operationally usable. Position objects retain operator-facing **Open / Closed** semantics while wire encoding and feedback decoding remain independently type-aware.

> ARSAS does not provide a generic control-write fallback for `.Oper`, `.SBOw`, or `.Cancel`. If the required native control contract is unavailable, the build or control readiness gate fails explicitly.

## Architecture at a glance

```text
┌────────────────────────────────────────────────────────────────────┐
│                            ARSAS                             │
│ Explorer · Live Monitor · Event Log · GOOSE · Diagnostics · Control│
└───────────────────────────────┬────────────────────────────────────┘
                                │ typed application services
┌───────────────────────────────▼────────────────────────────────────┐
│                           ARIEC61850                              │
│ MMS · Reporting · GOOSE · Npcap · Control · SCL · Diagnostics     │
└──────────────────────┬──────────────────────────┬──────────────────┘
                       │ IEC 61850 / TCP 102      │ EtherType 0x88B8
                  Laboratory IEDs            Station/process bus
```

ARSAS is the Windows application layer. The protocol implementation remains in the separately maintained [ARIEC61850](https://github.com/masarray/ARIEC61850) source repository and is referenced as sibling .NET projects at build time.

## Quick start

### Requirements

- Windows 10 or Windows 11
- .NET 8 SDK
- Visual Studio 2022 with **.NET desktop development**, or the .NET CLI
- A compatible ARIEC61850 source checkout
- Npcap for the GOOSE Subscriber tab; administrator rights may be required by the local Npcap installation policy
- An isolated laboratory or approved commissioning network for active functions and raw-Ethernet capture

### Recommended folder layout

```text
D:\Git\
├─ ARIEC61850\
│  └─ src\
│     ├─ AR.Iec61850\AR.Iec61850.csproj
│     └─ AR.Iec61850.Transports.Npcap\AR.Iec61850.Transports.Npcap.csproj
└─ ArIED61850Tester\
   └─ ArIED61850Tester.csproj
```

### Build

```powershell
git clone https://github.com/masarray/ARIEC61850.git
git clone https://github.com/masarray/ArIED61850Tester.git

cd ArIED61850Tester
dotnet restore .\ArIED61850Tester.csproj
dotnet build .\ArIED61850Tester.csproj -c Release
```

To use engine checkouts in another location:

```powershell
dotnet build .\ArIED61850Tester.csproj -c Release `
  -p:ArIec61850Project="D:\Engineering\ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj" `
  -p:ArIec61850NpcapProject="D:\Engineering\ARIEC61850\src\AR.Iec61850.Transports.Npcap\AR.Iec61850.Transports.Npcap.csproj"
```

### Create a portable Windows package

```powershell
.\scripts\publish-windows-portable.ps1 `
  -Version 1.6.16 `
  -EngineProject "D:\Engineering\ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj"
```

Expected output:

```text
dist\ArIED61850-1.6.16-win-x64-portable.zip
```

## Operational boundary

IEC 61850 control, report writes, temporary DataSet creation, and active network functions can affect equipment state or IED resources. Use active features only when:

- the test boundary, isolation, and switching authority are approved;
- the selected IED is in the intended test or maintenance condition;
- control models, feedback mapping, interlock/synchrocheck behavior, timeout, Cancel, and negative termination have been validated;
- another qualified person can independently verify the expected process response where required by the test plan.

GOOSE subscription is read-only, but capture still requires an approved adapter/network boundary. Confirm switch mirroring, VLAN/offload behavior, Npcap driver policy, and confidentiality before recording or exporting station traffic.

A successful command, report session, or GOOSE decode is protocol evidence for that test condition. It is not a universal interoperability, cybersecurity, functional-safety, or conformance claim.

## Documentation

| Document | Purpose |
|---|---|
| [Documentation hub](docs/README.md) | Starting point for engineering, validation, legal, and contribution documents. |
| [Architecture](docs/ARCHITECTURE.md) | Multi-IED ownership, acquisition strategy, runtime scaling, and timestamp semantics. |
| [GOOSE Subscriber](docs/GOOSE_SUBSCRIBER.md) | Npcap capture, stream supervision, ordered `allData` mapping, SCL/discovery binding, requirements, and field validation. |
| [Validation checklist](docs/VALIDATION_CHECKLIST.md) | Build, simulator, reporting, control, and live-test acceptance checks. |
| [Engine compatibility](ENGINE_COMPATIBILITY.md) | Required ARIEC61850 contracts and supported project-reference layout. |
| [Smart Control integration](ARIEC61850_SMART_CONTROL_INTEGRATION.md) | Native control-service integration details. |
| [Control feedback audit](SMART_CONTROL_FEEDBACK_AUDIT.md) | Feedback and completion evidence boundaries. |
| [Licensing](docs/LICENSING.md) | Current GPL community edition and separate commercial licensing path. |
| [Clean-room policy](docs/CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md) | Independent-development, fixture provenance, UI, and interoperability boundaries. |

## Contributing and support

Engineering contributions are welcome when they are focused, reproducible, independently authored, and free of confidential customer or employer material. Read [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), and [SUPPORT.md](SUPPORT.md) before opening a pull request or support issue.

For a failed connection, use **Diagnostics → Copy Diagnostic** and attach the sanitized report to the issue. Remove customer names, station identifiers, IP addressing that must remain private, credentials, packet captures, and confidential SCL content before sharing.

## License

The current `main` branch and current community release packages are licensed **only** under the [GNU General Public License v3.0 or later](LICENSE).

A separate negotiated commercial license is available for proprietary integration, OEM or white-label distribution, closed-source redistribution, warranty, maintenance, priority engineering support, training, and project-specific development. See [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md).

Project names, logos, icons, and official-release branding are handled separately from the software license. See [TRADEMARK.md](TRADEMARK.md).

Historical revisions through `0df1007d9538b978edba67218136bc5c4f8019ad` remain available under their original terms on branch `archive/apache-2.0-final`. Those historical terms apply only to those earlier revisions. See [docs/LICENSING.md](docs/LICENSING.md).

---

<div align="center">
  <strong>ARSAS</strong><br />
  Clear model evidence. Independent device sessions. Ordered GOOSE leaves. Guarded control workflows.
</div>
