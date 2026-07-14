# Contributing to ArIED 61850

Focused engineering contributions are welcome when they improve a reproducible application workflow and preserve the project's licensing, provenance, operational, and protocol boundaries.

## Good contribution candidates

- reproducible connection or model-discovery fixes;
- report-monitoring, event, diagnostics, project, or UI improvements;
- control-workflow fixes backed by simulator or synthetic evidence;
- performance and WPF virtualization improvements;
- documentation corrections with a clear evidence basis;
- deterministic tests and synthetic SCL fixtures;
- accessibility and keyboard-workflow improvements.

Open an issue before beginning a large architectural change so the application/engine boundary and validation plan can be agreed first.

## Development setup

Use a sibling checkout unless an explicit engine path is supplied:

```text
D:\Git\
├─ ARIEC61850\
│  └─ src\AR.Iec61850\AR.Iec61850.csproj
└─ ArIED61850Tester\
   └─ ArIED61850Tester.csproj
```

Build:

```powershell
dotnet restore .\ArIED61850Tester.csproj
dotnet build .\ArIED61850Tester.csproj -c Release
```

Run the source and provenance gate:

```powershell
.\scripts\verify-source-clean.ps1
```

## Pull-request expectations

A pull request should:

- explain the engineering problem and affected workflow;
- keep protocol implementation in ARIEC61850 rather than duplicating it in the WPF application;
- include focused validation steps and results;
- avoid unrelated formatting or generated-output changes;
- use synthetic or contributor-owned test data;
- update public documentation when behavior or claim boundaries change;
- preserve the current GPL-only community release wording.

For UI changes, include the tested Windows scaling level and keyboard workflow. For connection, reporting, or control changes, state whether validation used unit tests, loopback, simulator, or a laboratory IED.

## Contribution licensing

The current public project is distributed under `GPL-3.0-or-later` and maintains a separate commercial-licensing path. Before merge, contributors must:

- read and affirmatively agree to `CONTRIBUTOR-LICENSE-AGREEMENT.md`;
- sign off every commit under `DCO.txt`;
- have the legal right and any required employer authorization to contribute;
- avoid confidential, proprietary, restricted, or customer-owned material;
- identify any third-party component and its license before introducing it.

Example sign-off:

```text
Signed-off-by: Contributor Name <contributor@example.com>
```

## Clean-room and data provenance

External software may be used only as a lawfully licensed black-box interoperability endpoint. Do not use another implementation's source code, API composition, examples, tests, documentation wording, UI, internal structure, screenshots, or extracted resources as design material.

Do not submit raw customer or employer SCL, captures, logs, screenshots, or diagnostic exports. Reconstruct the issue with synthetic data or contributor-owned material whose redistribution rights are documented.

Read [docs/CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md](docs/CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md) before implementation work.

## Operational safety

Never perform live command or active network testing without the approved test boundary, target verification, isolation, and authority required by the site procedure. A successful software readiness check is not permission to operate equipment.

## Reporting security issues

Follow [SECURITY.md](SECURITY.md). Do not disclose exploitable details or sensitive project data in a public issue.
