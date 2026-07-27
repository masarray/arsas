<div align="center">
  <img src="Assets/app-icon.png" alt="ARSAS IEC 61850 engineering workstation logo" width="104" height="104" />

# ARSAS

### From an IED IP address to live evidence and interoperable SCL

**Discover · Monitor · Diagnose · Generate SCL · Integrate · Validate**

ARSAS is an open-source Windows IEC 61850 engineering workstation for FAT, SAT, commissioning, troubleshooting, and multi-vendor integration. Start from an approved IED IP address or an existing SCL file, discover what the device actually exposes, and preserve the result as attributable engineering evidence.

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
[**Compatibility evidence**](https://masarray.github.io/arsas/compatibility.html) ·
[**Roadmap**](ROADMAP.md)
</div>

<div align="center">
  <a href="Assets/screenshot/arsas%20(1).webp">
    <img src="Assets/screenshot/arsas%20(1).webp" alt="ARSAS IEC 61850 engineering workstation first-launch workspace" width="100%" />
  </a>
  <br />
  <sub>Start from an IED IP address, an SCL file, a saved project, or the built-in communication workspace.</sub>
</div>

## Why ARSAS stands out

| Engineering problem | ARSAS response |
|---|---|
| The vendor CID or ICD is missing, outdated, or rejected by another engineering tool. | Perform complete live MMS discovery from the IED IP address and generate a schema-aware **Edition 2 IID** or **Edition 1 ICD** with companion evidence. |
| Reporting setup consumes the test window before useful values appear. | Read selected values immediately, prefer verified BRCB/URCB coverage, recover specific gaps where permitted, and retain visible bounded polling when reporting cannot be trusted. |
| A multi-IED test loses device ownership and diagnostic context. | Keep an independent association, model, monitoring state, events, controls, files, and diagnostics for every IED while providing a unified project view. |
| Integration failures are reduced to vague messages such as “CID rejected” or “download failed.” | Preserve identity, references, DataSet membership, RCB options, protocol stages, negative responses, timing, and copyable diagnostic evidence. |

ARSAS is designed to shorten the path from **“the IED is reachable”** to **“the engineer has usable, attributable evidence.”** It does not hide unsupported behavior or turn an uncertain result into a false success.

## Built for real project work

- **FAT:** inspect models, selected signals, reports, GOOSE, controls, file services, and integration output before shipment.
- **SAT:** reconfirm installed endpoints, report behavior, station-bus traffic, and configured-versus-live differences.
- **Commissioning:** correlate live values, sequence of events, communication quality, fault records, and command completion.
- **Multi-vendor integration:** reconstruct or normalize bounded SCL output when vendor files, schema editions, identities, DataSets, or RCB names do not align cleanly.
- **Troubleshooting:** keep exact failures attached to the originating IED instead of rebuilding context across disconnected tools.

## IP address to interoperable SCL

A successful full discovery can become a reusable engineering baseline:

```text
Approved IED IP address
          ↓
IEC 61850 MMS association
          ↓
Complete typed live discovery
LD · LN · DO · DA · DataSet · RCB · types · identity
          ↓
Schema-aware normalization and validation
          ↓
Edition 2 IID or Edition 1 ICD
          ↓
Optional selected-RCB Edition 1/2 CID
          ↓
Target SAS or gateway import and FAT/SAT validation
```

### What ARSAS preserves

- the physical IED identity separately from complete MMS Logical Device domains;
- typed model hierarchy and available type information;
- DataSet membership and member order;
- exact concrete runtime RCB names;
- available `TrgOps`, `OptFlds`, `BufTm`, `IntgPd`, `RptID`, and `ConfRev` evidence;
- endpoint and source context;
- structured JSON evidence, Markdown summary, and export findings.

### What this helps prevent

- a Logical Device name being mistaken for the physical IED identity;
- valid MMS domains being shortened or rewritten;
- an exact indexed RCB name receiving a duplicate `01` suffix;
- incomplete FCDA membership being exported as if it were complete;
- Edition 1 and Edition 2 expectations being mixed silently;
- large vendor or project SCL files being passed downstream when only one bounded IED or one reporting path is required.

For an opened SCL source, ARSAS can also create a bounded generic single-IED Edition 2 output through the engine-owned interoperability converter while preserving the original file and surfacing structured findings.

> The output is vendor-neutral, typed, and schema-aware, and is intended to reduce common multi-vendor import failures. It is not a universal acceptance guarantee. Every IID, ICD, or CID must still be reviewed and validated in the receiving SAS, gateway, or engineering tool.

## Smart Reporting: useful values without a report dead end

```text
Selected signals
      ↓
Immediate MMS image
      ↓
Validate configured BRCB / URCB coverage
      ↓
Recover exact uncovered gaps where supported and permitted
      ↓
Keep rejected, occupied, empty, or unverified points visible in bounded polling
```

ARSAS records how every displayed value was acquired. The engineer can distinguish report delivery from polling, inspect DataSet coverage, preserve report reasons and timestamps, and see when a dynamic write or existing RCB cannot be used.

## Product tour

Every image below is captured from the working Windows application rather than a marketing mockup.

<table>
  <tr>
    <td width="50%" valign="top">
      <a href="Assets/screenshot/arsas%20(2).webp"><img src="Assets/screenshot/arsas%20(2).webp" alt="ARSAS connected simultaneously to multiple IEC 61850 IEDs" width="100%" /></a>
      <br /><strong>Independent multi-IED sessions</strong><br />Each device keeps its own association, discovered model, monitoring state, selected signals, files, controls, events, and diagnostics.
    </td>
    <td width="50%" valign="top">
      <a href="Assets/screenshot/arsas%20(3).webp"><img src="Assets/screenshot/arsas%20(3).webp" alt="ARSAS unified IEC 61850 live value viewer" width="100%" /></a>
      <br /><strong>Unified live-value workspace</strong><br />Search selected values while retaining device ownership, quality, timestamp, acquisition source, report reason, and recent-change evidence.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <a href="Assets/screenshot/arsas%20(4).webp"><img src="Assets/screenshot/arsas%20(4).webp" alt="ARSAS IEC 61850 sequence of events workspace" width="100%" /></a>
      <br /><strong>Sequence of events</strong><br />Correlate state transitions, report activity, command feedback, and process timestamps without losing the originating IED.
    </td>
    <td width="50%" valign="top">
      <a href="Assets/screenshot/arsas%20(5).webp"><img src="Assets/screenshot/arsas%20(5).webp" alt="ARSAS detailed IEC 61850 GOOSE analysis workspace" width="100%" /></a>
      <br /><strong>GOOSE supervision</strong><br />Inspect APPID, VLAN, MAC, <code>goCBRef</code>, DataSet, <code>stNum</code>, <code>sqNum</code>, TAL, retransmissions, ordered payload leaves, and model binding.
    </td>
  </tr>
</table>

<div align="center">
  <a href="Assets/screenshot/arsas%20(6).webp">
    <img src="Assets/screenshot/arsas%20(6).webp" alt="ARSAS IEC 61850 communication and frame diagnostics workspace" width="100%" />
  </a>
  <br />
  <strong>Attributable communication diagnostics</strong><br />
  <sub>Separate routing, TCP/102, association, discovery, reporting, file-service, GOOSE, timing, sequence, control, and device-behavior problems.</sub>
</div>

## Capability status

ARSAS uses explicit maturity labels so available behavior is not confused with roadmap direction.

| IEC 61850 area | Status | Current scope |
|---|---|---|
| **MMS client and model discovery** | Available | Association, physical identity, complete LD/LN/DO/DA hierarchy, values, quality, timestamps, DataSets, RCBs, type information, and communication diagnostics. |
| **Live discovery to SCL generation** | Available | Generate a single-IED Edition 2 IID or Edition 1 ICD from the last successful complete typed discovery of an IP-connected IED. |
| **SCL workspace and generic conversion** | Available | SCD/CID/ICD/IID/SSD import, endpoint extraction, configured/live context, generic single-IED Edition 2 conversion, schema-aware Edition 1 export, findings, and source preservation. |
| **Selected-RCB CID export** | Available | Read-only availability audit, exact live RCB name, verified DataSet members and live options, and one-RCB Edition 1/2 CID output. |
| **Reporting and live monitoring** | Available | BRCB/URCB inspection, immediate reads, exact coverage evaluation, bounded recovery where permitted, visible polling fallback, multi-IED monitoring, and SOE. |
| **GOOSE subscriber** | Available | Read-only Npcap capture, APPID/VLAN/MAC metadata, sequence and TAL supervision, ordered `allData`, timeline evidence, and SCL/live-model binding. |
| **IEC 61850 file transfer** | Available | Bounded MMS browsing and download, partial or grouped record handling, COMTRADE-related retrieval, reconnect boundaries, duplicate detection, and detailed transfer evidence. |
| **Smart Control** | Available | Live `ctlModel` discovery, typed Direct and SBO sequences, Test/interlock/synchrocheck context, CommandTermination, application errors, timing, and mapped feedback. |
| **Project persistence** | Available | Save selected signals and project context; retain a complete discovery snapshot for supported workflows while refusing to treat a signal-only cache as a complete export model. |
| **Sampled Values / SMV** | Engineering preview | Per-IED entry points and viewer workflow; deeper decoding, binding, scaling, synchronization, and sustained-performance validation remain active work. |
| **Full visual SCL authoring** | Planned | Create and edit complete project structures, communication, DataSets, control blocks, validation, diff, and reusable project output. |
| **Unified test-plan and evidence workspace** | Planned | Reusable FAT/SAT/commissioning steps, acceptance criteria, time-correlated evidence, review, and sanitized export packages. |

See [ROADMAP.md](ROADMAP.md) for milestones, definitions of done, and explicit non-goals.

## Current stable Windows release

The latest verified public release is **ARSAS v1.6.18**, published on **21 July 2026**.

| Package | Intended use |
|---|---|
| [Windows installer](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe) | Recommended for normal use, Start Menu integration, uninstall support, and the verified update workflow. |
| [Portable ZIP](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.zip) | Extract and run without installation on a controlled engineering workstation. |
| [SHA-256 checksums](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SHA256SUMS.txt) | Verify the exact package before use. |
| [Release details](https://github.com/masarray/arsas/releases/tag/v1.6.18) | Version scope, assets, publication evidence, and release notes. |

The installer checks the stable public manifest, validates the expected asset identity, size, and SHA-256, and only then opens the downloaded installer. Current public binaries are **not Authenticode-signed**, so Windows SmartScreen may show an unrecognized-publisher warning.

## Start using ARSAS

### System requirements

- Windows 10 or Windows 11, x64.
- An isolated laboratory network or an approved FAT, SAT, or commissioning boundary.
- Ethernet access to the intended IED for MMS services, normally TCP port 102.
- [Npcap](https://npcap.com/) only for raw-Ethernet GOOSE or Sampled Values capture.
- Suitable authorization, switching authority, procedures, and independent verification before any active control or configuration workflow.

Visual Studio and an ARIEC61850 source checkout are **not required** for normal use of the packaged release.

### First read-only session

1. Download the installer or portable ZIP and verify its SHA-256.
2. Confirm the approved network path and TCP port 102.
3. Choose **Add IED by IP address**.
4. Complete live MMS discovery.
5. Select a small known set of read-only signals.
6. Verify device ownership, value, quality, timestamp, and acquisition source.
7. Expand to reporting, GOOSE, file transfer, SCL generation, or control only after the read path is understood.

### Generate SCL from the IED

1. Complete a successful full discovery or **Re-scan**.
2. Confirm the physical IED identity and review blocking discovery diagnostics.
3. Choose **Save SCL** on the IED card.
4. Select **Edition 2** for `.iid` or **Edition 1** for `.icd`.
5. Review the SCL, JSON evidence, Markdown summary, and warnings.
6. Import and validate the result in the target SAS or gateway.

For a bounded legacy integration, open **RCB**, run the read-only availability audit, select exactly one usable RCB, and generate an Edition 1 or Edition 2 CID baseline.

### Explore without a live IED

Press **Ctrl+Shift+D** while no real IED session or GOOSE capture is active. The built-in communication workspace helps users explore the UI and workflow. It is generated presentation data—not a relay simulator, compatibility result, performance benchmark, or conformance proof.

See the bilingual [Quick Start](https://masarray.github.io/arsas/quick-start.html), [Guided Demo](https://masarray.github.io/arsas/demo.html), [FAQ](https://masarray.github.io/arsas/faq.html), and [Troubleshooting Guides](https://masarray.github.io/arsas/guides.html).

## Core engineering workflows

### Live MMS engineering

- Maintain independent device sessions and lifecycle state.
- Discover the complete typed hierarchy and available services.
- Search large models without flattening away device ownership.
- Keep the complete model attached even when the operator view hides non-operational attributes.
- Compare configured SCL intent with current live evidence.

### Selected-RCB integration export

- Audit availability without enabling, disabling, or reserving an RCB.
- Retain verified live DataSet members and configuration evidence.
- Preserve exact runtime RCB names and prevent duplicate indexing.
- Export a bounded Edition 1/2 CID while keeping original sources unchanged.

### GOOSE supervision

- Capture receive-only Ethernet frames through the ARIEC61850 Npcap transport.
- Inspect identity, addressing, sequence, retransmission, TAL, flags, and ordered payload.
- Bind streams to SCL or live discovery where evidence permits.
- Surface mismatches rather than silently truncating data.

### Fault-record retrieval

- Browse the selected IED MMS file service with bounded operations.
- Download complete, partial, extensionless, or single-file records actually exposed by the device.
- Support signed FRSM handles, segmented responses, reconnect checks, progress, cancellation, and safe temporary files.
- Detect local record state and provide copyable transfer diagnostics.

### Guarded control

- Discover the live `ctlModel` and exact control descriptors.
- Execute supported Direct or Select-Before-Operate sequences.
- Preserve origin, `ctlNum`, timestamp, Test, interlock, and synchrocheck context.
- Surface CommandTermination, application errors, timing, and mapped process feedback.

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
│ Transport · Type System · Schema Profiles · Diagnostics · Validation  │
└─────────────────────────┬──────────────────────┬───────────────────────┘
                          │ TCP/102              │ Ethernet process bus
                     Approved IEDs          Approved capture network
```

Protocol parsing, transport behavior, typed contracts, schema profiles, SCL conversion/export, and reusable validation belong in [ARIEC61850](https://github.com/masarray/ARIEC61850). ARSAS owns the Windows workflow, project state, visualization, diagnostics, and engineer-facing evidence.

Detailed notes: [Architecture](docs/ARCHITECTURE.md) · [Engine compatibility](ENGINE_COMPATIBILITY.md)

## Build from source

### Developer requirements

- Windows 10 or Windows 11.
- .NET 8 SDK.
- Visual Studio 2022 with **.NET desktop development**, or the .NET CLI.
- A compatible [ARIEC61850](https://github.com/masarray/ARIEC61850) source checkout.
- Npcap when building or testing raw-Ethernet workflows.

Recommended layout:

```text
D:\Git\
├─ ARIEC61850\
│  └─ src\
│     ├─ AR.Iec61850\AR.Iec61850.csproj
│     └─ AR.Iec61850.Transports.Npcap\AR.Iec61850.Transports.Npcap.csproj
└─ arsas\
   └─ ArIED61850Tester.csproj
```

```powershell
git clone https://github.com/masarray/ARIEC61850.git
git clone https://github.com/masarray/arsas.git
cd arsas
dotnet restore .\ArIED61850Tester.csproj
dotnet build .\ArIED61850Tester.csproj -c Release
```

For a non-sibling engine checkout, pass `ArIec61850Project` and `ArIec61850NpcapProject` explicitly.

## Documentation and support

| Resource | Purpose |
|---|---|
| [Product website](https://masarray.github.io/arsas/) | Product overview, workflows, evidence, Indonesian navigation, and downloads. |
| [Quick Start](https://masarray.github.io/arsas/quick-start.html) | Verified package to first live value and IP-to-SCL continuation. |
| [Compatibility evidence](https://masarray.github.io/arsas/compatibility.html) | Service-level evidence with version, date, conditions, and bounded conclusions. |
| [Guides](https://masarray.github.io/arsas/guides.html) | Symptom-led troubleshooting for port 102, reporting, RCBs, files, GOOSE, SCL, and control. |
| [Download Center](https://masarray.github.io/arsas/download.html) | Installer/portable comparison, hashes, requirements, and stable release scope. |
| [Documentation hub](docs/README.md) | Engineering, validation, legal, contribution, and operational documents. |
| [Support guide](SUPPORT.md) | Required troubleshooting evidence and sanitization. |
| [Security policy](SECURITY.md) | Responsible private vulnerability reporting. |

A successful service observation is not a universal vendor conclusion or an IEC 61850 conformance certificate.

## Safety, privacy, and evidence boundaries

IEC 61850 control, report configuration, temporary DataSet creation, file access, and other active operations can affect IED resources or equipment state. Use them only inside an approved boundary with suitable isolation, authority, procedures, and independent verification.

GOOSE and Sampled Values capture is receive-only in ARSAS, but station traffic still requires an approved adapter, network boundary, and data-handling policy.

Do not publish credentials, private endpoints, customer identity, confidential SCL, relay settings, packet captures, disturbance records, or employer material. Sanitize evidence before opening a public issue.

ARSAS is not:

- an IEC 61850 conformance certificate;
- functional-safety certification;
- cybersecurity approval for an operational network;
- automatic switching authority or proof of safe isolation;
- a guarantee of acceptance by every proprietary SCL importer;
- a substitute for an approved FAT, SAT, commissioning, or operating procedure.

## Contributing

Focused, reproducible, independently authored contributions are welcome. Strong contributions include exact sanitized diagnostics, bounded reproduction steps, fixtures with clear provenance, expected behavior, validation evidence, and a clear application-versus-engine boundary.

Read [CONTRIBUTING.md](CONTRIBUTING.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md), [SECURITY.md](SECURITY.md), [SUPPORT.md](SUPPORT.md), and [NOTICE.md](NOTICE.md).

## License

The community edition is licensed under **GNU GPL v3.0 or later**. See [LICENSE](LICENSE).

A separately negotiated commercial license is available for proprietary integration, OEM or white-label distribution, closed-source redistribution, warranty, maintenance, priority support, training, and project-specific development. See [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md).

Names, logos, icons, and official-release branding are not granted by the software license. See [TRADEMARK.md](TRADEMARK.md) and [NOTICE.md](NOTICE.md).

---

<div align="center">
  <strong>ARSAS</strong><br />
  From an approved IED IP address to trustworthy IEC 61850 evidence and reusable integration output.
</div>
