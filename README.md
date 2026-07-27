<div align="center">
  <img src="Assets/app-icon.png" alt="ARSAS IEC 61850 engineering workstation logo" width="104" height="104" />

# ARSAS

### Open-source IEC 61850 testing and engineering workstation for Windows

**MMS · Reporting · Multi-IED Monitoring · GOOSE · File Transfer · SCL · Control · Diagnostics · Sampled Values**

ARSAS turns an approved IED endpoint or SCL file into live IEC 61850 engineering evidence: discovered models, selected values, report activity, sequence of events, GOOSE streams, fault records, SCL export context, guarded control outcomes, and attributable diagnostics.

[![Build](https://github.com/masarray/arsas/actions/workflows/build.yml/badge.svg)](https://github.com/masarray/arsas/actions/workflows/build.yml)
[![Pages](https://github.com/masarray/arsas/actions/workflows/pages.yml/badge.svg)](https://masarray.github.io/arsas/)
[![Release](https://img.shields.io/github/v/release/masarray/arsas?display_name=tag&sort=semver&label=stable)](https://github.com/masarray/arsas/releases/latest)
[![License](https://img.shields.io/badge/license-GPL--3.0--or--later-2563eb)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512bd4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0ea5e9)](#system-requirements)

[**Download installer**](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe) ·
[**Portable ZIP**](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip) ·
[**Quick Start**](https://masarray.github.io/arsas/quick-start.html) ·
[**Product website**](https://masarray.github.io/arsas/) ·
[**Release notes**](https://masarray.github.io/arsas/release-notes.html) ·
[**Roadmap**](ROADMAP.md) ·
[**Report an issue**](https://github.com/masarray/arsas/issues)
</div>

<div align="center">
  <a href="Assets/screenshot/arsas%20(1).webp">
    <img src="Assets/screenshot/arsas%20(1).webp" alt="ARSAS IEC 61850 engineering workstation first-launch workspace" width="100%" />
  </a>
  <br />
  <sub>Start from an IED address, an SCL file, a saved project, or the built-in communication demonstration workspace.</sub>
</div>

## Current stable release

The latest verified public Windows release is **ARSAS v1.6.18**, published on **21 July 2026**.

| Package | Intended use |
|---|---|
| [Windows installer](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe) | Recommended for normal use, Start Menu integration, uninstall support, and the application update workflow. |
| [Portable ZIP](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip) | Extract and run without installation; useful for controlled engineering workstations and temporary test environments. |
| [SHA-256 checksums](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SHA256SUMS.txt) | Verify the exact downloaded installer or portable package before use. |
| [Release details](https://github.com/masarray/arsas/releases/tag/v1.6.18) | Version scope, assets, publication date, and release evidence. |

The current public binaries are **not Authenticode code-signed**. Windows SmartScreen may display an unrecognized-publisher warning. Verify the SHA-256 checksum and download only from the official GitHub release.

The installer build includes a stable-channel update workflow. ARSAS reads the public release manifest, validates the expected package identity, size, and SHA-256 value, and only then opens the downloaded installer.

## What changed in v1.6.18

The README previously described an earlier development foundation. The current stable release adds important field-oriented behavior:

- **More reliable MMS fault-record retrieval** with adaptive remote-path handling, large segmented response support, signed FRSM compatibility, bounded reconnect behavior, automatic scanning, redownload/overwrite handling, and directly copyable transfer diagnostics.
- **Partial and vendor-specific record support** so available files can be downloaded even when a complete COMTRADE companion set is not exposed by the IED.
- **Selected-RCB CID export** with read-only availability checking, exact live RCB-name preservation, live configuration evidence, and IEC 61850 Edition 1 or Edition 2 output profiles.
- **IED identity correction** that separates the physical IED identity from complete MMS Logical Device domain names instead of collapsing or rewriting valid model domains.
- **Cleaner field UX** for fault-record states, retry feedback, local duplicate detection, compact RCB selection, progress, warnings, and successful export evidence.
- **Verified Windows publication** with installer and portable packages, silent installer smoke tests, SHA-256 checksums, a stable updater manifest, and reviewed release metadata.

Development after the v1.6.18 package has also expanded the public adoption layer with bilingual Quick Start and FAQ routes, a guided screenshot-based demo, bounded compatibility evidence, local troubleshooting filters, structured issue forms, responsive media, and supply-chain evidence contracts for future release outputs.

> The current v1.6.18 release has verified assets and checksums, but it does not make a retroactive code-signing, SBOM, provenance, vendor-wide compatibility, or IEC 61850 conformance claim.

## Why ARSAS

IEC 61850 FAT, SAT, commissioning, and troubleshooting often lose time to disconnected tools, repeated model preparation, manual DataSet and RCB inspection, uncertain report coverage, and weak evidence when a service fails.

ARSAS is built as one focused workflow:

```text
IED endpoint / SCL file / saved project
                 ↓
Discover or restore the IEC 61850 model
                 ↓
Select values and control-ready objects
                 ↓
Use reports first, with visible bounded fallback
                 ↓
Correlate MMS values, SOE, GOOSE, files and diagnostics
                 ↓
Retrieve fault records or export selected-RCB SCL context
                 ↓
Validate guarded control and preserve engineering evidence
```

The reusable protocol engine is maintained separately in [ARIEC61850](https://github.com/masarray/ARIEC61850). ARSAS owns the Windows application, workflow orchestration, visualization, project experience, and engineer-facing evidence.

## See the working application

Every image below is captured from the real Windows application rather than a marketing mockup.

<table>
  <tr>
    <td width="50%" valign="top">
      <a href="Assets/screenshot/arsas%20(2).webp"><img src="Assets/screenshot/arsas%20(2).webp" alt="ARSAS connected simultaneously to multiple IEC 61850 IEDs" width="100%" /></a>
      <br /><strong>Independent multi-IED sessions</strong><br />Each protection relay or bay-control device keeps its own association, discovered model, monitoring state, selected signals, and diagnostics.
    </td>
    <td width="50%" valign="top">
      <a href="Assets/screenshot/arsas%20(3).webp"><img src="Assets/screenshot/arsas%20(3).webp" alt="ARSAS unified IEC 61850 live value viewer" width="100%" /></a>
      <br /><strong>Unified live-value workspace</strong><br />Search selected values across IEDs while retaining quality, timestamps, acquisition source, report reason, and recent-change evidence.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <a href="Assets/screenshot/arsas%20(4).webp"><img src="Assets/screenshot/arsas%20(4).webp" alt="ARSAS IEC 61850 sequence of events workspace" width="100%" /></a>
      <br /><strong>SCADA-style sequence of events</strong><br />Correlate state transitions and report activity without losing the originating IED or process timestamp.
    </td>
    <td width="50%" valign="top">
      <a href="Assets/screenshot/arsas%20(5).webp"><img src="Assets/screenshot/arsas%20(5).webp" alt="ARSAS detailed IEC 61850 GOOSE analysis workspace" width="100%" /></a>
      <br /><strong>Detailed GOOSE supervision</strong><br />Inspect APPID, VLAN, MAC, <code>goCBRef</code>, DataSet, <code>stNum</code>, <code>sqNum</code>, TAL, retransmissions, ordered payload leaves, and model binding.
    </td>
  </tr>
</table>

<div align="center">
  <a href="Assets/screenshot/arsas%20(6).webp">
    <img src="Assets/screenshot/arsas%20(6).webp" alt="ARSAS IEC 61850 communication and frame diagnostics workspace" width="100%" />
  </a>
  <br />
  <strong>Attributable communication diagnostics</strong><br />
  <sub>Separate routing, TCP/102, association, model, report, file-service, GOOSE, timing, sequence, and device-behavior problems without losing the originating session context.</sub>
</div>

## Capability status

ARSAS uses explicit maturity labels so implemented behavior is not confused with long-term product direction.

| IEC 61850 area | Status | Current scope |
|---|---|---|
| **MMS client and model discovery** | Available | Association, identity, Logical Devices, Logical Nodes, Data Objects, Data Attributes, values, quality, timestamps, DataSets, RCBs, type information, and communication diagnostics. |
| **Reporting and live monitoring** | Available | Existing BRCB/URCB inspection, report-first acquisition, exact coverage evaluation, bounded gap recovery where permitted, visible polling fallback, persisted selections, multi-IED monitoring, and SOE. |
| **GOOSE subscriber** | Available | Read-only Npcap capture, stream supervision, APPID/VLAN/MAC metadata, `stNum`/`sqNum`, TAL, retransmissions, ordered `allData`, timeline evidence, and SCL/live-model binding. |
| **IEC 61850 file transfer** | Available | Bounded MMS directory browsing and download, partial or grouped record handling, COMTRADE-related retrieval, progress, retry/reconnect boundaries, local duplicate detection, and detailed transfer evidence. Interoperability coverage continues to expand. |
| **SCL workspace and selected-RCB export** | Available | SCD/CID/ICD/IID/SSD import, endpoint extraction, configured/live context, read-only RCB availability audit, and selected-RCB Edition 1/2 CID export. |
| **Smart Control** | Available | Live `ctlModel` discovery, typed Direct and Select-Before-Operate sequences, Test/interlock/synchrocheck context, command termination, application-error evidence, and mapped process feedback. |
| **Sampled Values / SMV** | Engineering preview | Per-IED entry points and viewer workflow; deeper decoding, SCL binding, quality supervision, timing validation, and performance work remain active. |
| **Full visual SCL authoring** | Planned | Create and edit IED, communication, DataSet, report, GOOSE, Sampled Values, validation, diff, and reusable project output. |
| **Unified test-plan and evidence workspace** | Planned | Reusable FAT/SAT/commissioning steps, linked acceptance criteria, time-correlated evidence, review, and sanitized export packages. |

See [ROADMAP.md](ROADMAP.md) for status definitions, milestones, definitions of done, and explicit non-goals.

## Start using ARSAS

### System requirements

- Windows 10 or Windows 11, x64.
- An isolated laboratory network or an approved commissioning boundary.
- Ethernet access to the intended IED for MMS services on TCP port 102.
- [Npcap](https://npcap.com/) only when raw-Ethernet GOOSE or Sampled Values capture is required.
- Suitable authorization, switching authority, procedures, and independent verification before any active control or configuration workflow.

A Visual Studio installation and ARIEC61850 source checkout are **not required for normal use of the packaged Windows release**. They are only required when building the project from source.

### Install or run portable

1. Download the [installer](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe) or [portable ZIP](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip).
2. Download [ARSAS-Windows-x64-SHA256SUMS.txt](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SHA256SUMS.txt).
3. Verify the selected file:

```powershell
Get-FileHash .\ARSAS-Windows-x64-Setup.exe -Algorithm SHA256
# or
Get-FileHash .\ARSAS-Windows-x64-Portable.zip -Algorithm SHA256
```

4. Compare the result with the official checksum file.
5. Install ARSAS, or extract the portable ZIP to a writable local folder and run `ARSAS.exe`.
6. Install Npcap only when GOOSE or Sampled Values capture is needed.

### First read-only live session

1. Confirm that the endpoint is authorized and isolated appropriately.
2. Verify IP routing and TCP port 102 independently.
3. Open ARSAS and choose **Add IED by IP address**.
4. Connect and allow ARSAS to discover the live MMS model.
5. Select a small set of signals first.
6. Start monitoring and verify value, quality, timestamp, source, and diagnostics.
7. Expand to reporting, GOOSE, file transfer, SCL export, or control only after the read-only path is understood.

See the bilingual [Quick Start](https://masarray.github.io/arsas/quick-start.html) and [FAQ](https://masarray.github.io/arsas/faq.html) for setup, Npcap, SmartScreen, compatibility, safety, privacy, and troubleshooting guidance.

### Explore without a live IED

Press **Ctrl+Shift+D** while no IED is connected and GOOSE capture is stopped to load the built-in communication demonstration workspace.

The demonstration workspace is intended for UI exploration, screenshots, training orientation, and workflow review. It uses generated presentation data; it is **not** a relay simulator, packet generator, time-to-value benchmark, compatibility result, or protocol-conformance proof.

## Core engineering workflows

### Live MMS engineering

- Maintain an independent association and lifecycle for every IED.
- Discover the model hierarchy, values, quality, timestamps, DataSets, RCBs, control blocks, and type descriptors.
- Search and virtualize large signal models without flattening away IED ownership.
- Keep configured SCL context beside observed live behavior.
- Copy bounded, sanitized connection and protocol diagnostics for support.

### Smart Reporting

- Begin with an immediate MMS image while report coverage is evaluated.
- Prefer usable existing BRCBs and URCBs before polling.
- Preserve DataSet order, report reason, sequence, and source evidence.
- Recover exact uncovered gaps only where the device supports it and writes are permitted.
- Keep rejected, occupied, empty, or unverified coverage visible in bounded polling rather than creating a silent dead end.
- Coalesce UI updates to remain responsive across multiple devices.

### GOOSE supervision

- Capture read-only Ethernet frames through the ARIEC61850 Npcap transport.
- Inspect stream identity, APPID, VLAN, destination MAC, sequence, retransmission, TAL, and payload order.
- Bind observed frames to SCL or live model context where available.
- Preserve mismatches and sequence anomalies instead of silently truncating data.

### Fault-record retrieval

- Browse the selected IED's MMS file service with bounded operations.
- Recognize related COMTRADE and vendor companion files without making the transport file-type specific.
- Download complete, partial, extensionless, or single-file records when the IED actually exposes them.
- Use dedicated session lifecycle checks, signed FRSM compatibility, segmented response handling, progress, cancellation, and safe temporary files.
- Detect local completed or incomplete records and support deliberate redownload or overwrite.
- Copy exact sanitized failure evidence, including stage, session state, request/response context, and remote paths.

### SCL context and selected-RCB CID export

- Open common SCL file types and extract available endpoints.
- Retain configured intent beside live model evidence.
- Audit live RCB availability without reserving, enabling, or modifying the RCB.
- Select one intended RCB and preserve its exact runtime name, DataSet members, trigger options, optional fields, buffer time, integrity period, report ID, and configuration revision where available.
- Export an IEC 61850 Edition 1 or Edition 2 CID baseline for controlled SAS or gateway integration.
- Keep full visual SCL project authoring explicitly on the roadmap.

### Guarded control with evidence

- Discover the live `ctlModel` before enabling an operation.
- Resolve supported `Oper`, `SBOw`, and optional `Cancel` descriptors.
- Execute typed Direct or Select-Before-Operate sequences for supported control objects.
- Preserve origin, `ctlNum`, timestamp, Test, interlock, and synchrocheck context.
- Surface `CommandTermination`, `ControlError`, `AddCause`, `LastApplError`, timing, and mapped process feedback.
- Keep command authorization and physical process safety outside the software's claims.

## Architecture

```text
┌────────────────────────────────────────────────────────────────────────┐
│                                 ARSAS                                  │
│ Explorer · Monitor · SOE · GOOSE · SMV · Files · SCL · Control · UX  │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │ typed application services
┌──────────────────────────────────▼─────────────────────────────────────┐
│                              ARIEC61850                                │
│ MMS · Reporting · GOOSE · SMV · File Services · SCL · Control        │
│ Transport · Type System · Diagnostics · Protocol Validation           │
└─────────────────────────┬──────────────────────┬───────────────────────┘
                          │ TCP/102              │ Ethernet process bus
                     Approved IEDs          Approved capture network
```

Protocol parsing, transport behavior, typed service contracts, and reusable validation belong in ARIEC61850. ARSAS consumes those contracts and owns engineer-facing workflow, state, visualization, project persistence, diagnostics, and evidence presentation.

Detailed design notes are maintained in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [ENGINE_COMPATIBILITY.md](ENGINE_COMPATIBILITY.md).

## Build from source

### Developer requirements

- Windows 10 or Windows 11.
- .NET 8 SDK.
- Visual Studio 2022 with **.NET desktop development**, or the .NET CLI.
- A compatible [ARIEC61850](https://github.com/masarray/ARIEC61850) source checkout.
- Npcap when building or testing raw-Ethernet GOOSE and Sampled Values workflows.

### Recommended folder layout

```text
D:\Git\
├─ ARIEC61850\
│  └─ src\
│     ├─ AR.Iec61850\AR.Iec61850.csproj
│     └─ AR.Iec61850.Transports.Npcap\AR.Iec61850.Transports.Npcap.csproj
└─ arsas\
   └─ ArIED61850Tester.csproj
```

### Build

```powershell
git clone https://github.com/masarray/ARIEC61850.git
git clone https://github.com/masarray/arsas.git

cd arsas
dotnet restore .\ArIED61850Tester.csproj
dotnet build .\ArIED61850Tester.csproj -c Release
```

For a non-sibling engine checkout:

```powershell
dotnet build .\ArIED61850Tester.csproj -c Release `
  -p:ArIec61850Project="D:\Engineering\ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj" `
  -p:ArIec61850NpcapProject="D:\Engineering\ARIEC61850\src\AR.Iec61850.Transports.Npcap\AR.Iec61850.Transports.Npcap.csproj"
```

## Documentation and support

| Resource | Purpose |
|---|---|
| [Product website](https://masarray.github.io/arsas/) | Product overview, capabilities, engineering solutions, architecture, guides, and Indonesian-language navigation. |
| [Quick Start](https://masarray.github.io/arsas/quick-start.html) | Seven-step onboarding from package verification to the first bounded live-value workflow. |
| [FAQ](https://masarray.github.io/arsas/faq.html) | Installation, SmartScreen, Npcap, compatibility, safety, privacy, and usage answers. |
| [Download Center](https://masarray.github.io/arsas/download.html) | Installer versus portable guidance, checksums, requirements, and stable release scope. |
| [Release notes](https://masarray.github.io/arsas/release-notes.html) | Current user-facing changes, reliability improvements, and known limitations. |
| [Machine-readable release manifest](landing/latest.json) | Stable version, source commit, package URLs, sizes, hashes, and signing status used by the updater. |
| [Roadmap](ROADMAP.md) | Available, engineering-preview, planned, and research milestones with definitions of done. |
| [Documentation hub](docs/README.md) | Engineering, validation, legal, contribution, and operational documents. |
| [Architecture](docs/ARCHITECTURE.md) | Runtime ownership, acquisition strategy, protocol boundaries, and scale. |
| [GOOSE Subscriber](docs/GOOSE_SUBSCRIBER.md) | Capture, ordered data, model binding, diagnostics, and validation. |
| [Validation checklist](docs/VALIDATION_CHECKLIST.md) | Build, reporting, control, simulator, and live-test acceptance checks. |
| [Engine compatibility](ENGINE_COMPATIBILITY.md) | Required ARIEC61850 contracts and project-reference layout. |
| [Support guide](SUPPORT.md) | Troubleshooting evidence and mandatory data sanitization before reporting an issue. |
| [Security policy](SECURITY.md) | Responsible private vulnerability reporting. |

Compatibility evidence is recorded per service, ARSAS version, test date, conditions, and observed result. A successful service observation is not a universal vendor conclusion or an IEC 61850 conformance certificate.

## Safety, privacy, and evidence boundaries

IEC 61850 control, report configuration, temporary DataSet creation, file access, and other active network functions can affect IED resources or equipment state. Use active features only inside an approved boundary with suitable isolation, switching authority, procedures, and independent verification.

GOOSE and Sampled Values capture is read-only in ARSAS, but packet capture still requires an approved adapter, network boundary, data-handling policy, and permission to inspect or export station traffic.

Do not publish credentials, private endpoints, customer identity, confidential SCL files, relay settings, packet captures, disturbance records, or employer material. Sanitize evidence before opening a public issue.

ARSAS is an engineering tool. It is not:

- an IEC 61850 conformance certificate;
- functional-safety certification;
- cybersecurity approval for an operational station network;
- automatic switching authority or proof of safe isolation;
- a guarantee of universal interoperability;
- a substitute for an approved FAT, SAT, commissioning, or operating procedure.

## Contributing

Focused, reproducible, independently authored contributions are welcome. Strong contributions include bounded reproduction steps, protocol fixtures with clear provenance, exact sanitized diagnostics, expected behavior, validation evidence, and a clear application-versus-engine boundary.

Read [CONTRIBUTING.md](CONTRIBUTING.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md), [SECURITY.md](SECURITY.md), [SUPPORT.md](SUPPORT.md), and [NOTICE.md](NOTICE.md) before contributing or redistributing the software.

## License

The current community edition is licensed under the **GNU General Public License v3.0 or later**. See [LICENSE](LICENSE).

A separately negotiated commercial license is available for proprietary integration, OEM or white-label distribution, closed-source redistribution, warranty, maintenance, priority support, training, and project-specific development. See [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md).

Names, logos, icons, and official-release branding are not granted by the software license. See [TRADEMARK.md](TRADEMARK.md) and [NOTICE.md](NOTICE.md).

---

<div align="center">
  <strong>ARSAS</strong><br />
  From an approved endpoint or SCL file to trustworthy IEC 61850 evidence—in one Windows engineering workspace.
</div>
