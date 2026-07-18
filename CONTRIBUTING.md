# Contributing to ARSAS

Focused engineering contributions are welcome when they improve a reproducible IEC 61850 workflow and preserve the project's licensing, provenance, operational-safety, cybersecurity, and application/engine boundaries.

## Good contribution candidates

- reproducible MMS connection or model-discovery fixes;
- reporting, monitoring, SOE, diagnostics, GOOSE, Sampled Values, file-transfer, SCL, control, project, or UI improvements;
- control-workflow fixes backed by simulator, synthetic, or authorized laboratory evidence;
- performance, allocation, threading, and WPF virtualization improvements;
- documentation corrections with a clear engineering basis;
- deterministic tests and synthetic SCL, protocol, or file-service fixtures;
- accessibility, keyboard workflow, high-DPI, and localization-readiness improvements.

Open an issue before beginning a large architectural change so the ARSAS/ARIEC61850 boundary, maturity label, safety impact, and validation plan can be agreed first.

## Development setup

Use a sibling checkout unless an explicit engine path is supplied:

```text
D:\Git\
├─ ARIEC61850\
│  └─ src\AR.Iec61850\AR.Iec61850.csproj
└─ arsas\
   └─ ArIED61850Tester.csproj
```

Build:

```powershell
dotnet restore .\ArIED61850Tester.csproj
dotnet build .\ArIED61850Tester.csproj -c Release
```

Run the website and source/provenance gates:

```powershell
python .\scripts\validate-landing.py
.\scripts\verify-source-clean.ps1
```

## Application and engine boundary

- ARSAS owns projects, device-session orchestration, visualization, UX, evidence presentation, and application persistence.
- ARIEC61850 owns reusable transport, association, MMS, reporting, GOOSE, Sampled Values, file services, SCL, control, type handling, and protocol diagnostics.
- Do not duplicate protocol rules in WPF event handlers merely to make a UI feature appear complete.
- New engine contracts should be typed, bounded, independently testable, and explicit about unsupported behavior.

## Pull-request expectations

A pull request should:

- explain the engineering problem and affected workflow;
- state whether the capability is available, engineering preview, planned, or unchanged;
- keep protocol implementation in ARIEC61850 where it belongs;
- include focused validation steps and results;
- avoid unrelated formatting or generated-output changes;
- use synthetic, public-domain, or contributor-owned test data;
- update public documentation when behavior, maturity, safety, or claim boundaries change;
- preserve the GPL community license and separate commercial-licensing wording;
- include a signed-off commit as required by the project DCO.

For UI changes, include the tested Windows scaling level, resolution, keyboard workflow, and any accessibility impact. For protocol, reporting, GOOSE, SMV, file-transfer, SCL, or control changes, state whether validation used unit tests, deterministic fixtures, loopback, simulator, or an authorized laboratory IED.

## Contribution licensing

The current public project is distributed under `GPL-3.0-or-later` and maintains a separate commercial-licensing path. Before merge, contributors must:

- read and affirmatively agree to [CONTRIBUTOR-LICENSE-AGREEMENT.md](CONTRIBUTOR-LICENSE-AGREEMENT.md);
- sign off every commit under [DCO.txt](DCO.txt);
- have the legal right and any required employer authorization to contribute;
- avoid confidential, proprietary, restricted, customer-owned, or security-sensitive material;
- identify any third-party component and its license before introducing it.

Example sign-off:

```text
Signed-off-by: Contributor Name <contributor@example.com>
```

## Clean-room and data provenance

External software may be used only as a lawfully licensed black-box interoperability endpoint. Do not use another implementation's source code, API composition, examples, tests, documentation wording, UI, internal structure, screenshots, or extracted resources as design material.

Do not submit raw customer or employer SCL, relay settings, captures, logs, screenshots, diagnostic exports, credentials, file-service contents, or station-identifying data. Reconstruct the issue with synthetic material or contributor-owned data whose redistribution rights are documented.

Read [docs/CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md](docs/CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md) before implementation work.

## Operational safety

Never perform live command, report-write, file-access, or active network testing without the approved test boundary, target verification, isolation, cybersecurity permission, and authority required by the site procedure. A successful software readiness check is not permission to operate equipment.

## Reporting security issues

Follow [SECURITY.md](SECURITY.md). Do not disclose exploitable details, credentials, private endpoints, or sensitive project data in a public issue.
