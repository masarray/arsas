# ARSAS Documentation

This directory contains the engineering, validation, licensing, provenance, release, and operating-boundary documents for the ARSAS Windows IEC 61850 engineering workstation.

The current application version on `main` is **ARSAS 1.6.38**. Public binary identity remains tied to the latest actually published stable GitHub Release and its verified package evidence.

## Start here

| Document | Purpose |
|---|---|
| [Project README](../README.md) | Product overview, current-version summary, feature scope, quick start, build instructions, and public claim boundary. |
| [IO List FAT Evidence Testing](IO_LIST_FAT_EVIDENCE.md) | Imported SDI test plans, OFF → ON → OFF evidence, Excel/PDF export, portable `.arsas` projects, integrity checks, and cross-laptop continuation. |
| [Architecture](ARCHITECTURE.md) | Multi-IED ownership, model identity, report-first acquisition, runtime scaling, and timestamp semantics. |
| [Interoperability convergence](INTEROPERABILITY_CONVERGENCE.md) | R10 physical discovery, trusted-SCL reuse, report-backed acquisition, exact-case value identity, and 1.6.38 release verification. |
| [SCL export](SCL_EXPORT.md) | Live-discovery and source-backed SCL export, including the 1.6.38 logical ReportControl versus runtime RCB-instance boundary. |
| [COMTRADE viewer integration](COMTRADE_VIEWER_INTEGRATION.md) | Native ArdIrec bridge, in-process cursor/Phasor/Harmonics/Locus analysis, packaging, and presentation-only easing contract. |
| [Windows releases](WINDOWS_RELEASES.md) | Installer and portable single-EXE packaging, exact release gates, checksums, SBOM, provenance, attestations, and publication boundary. |
| [GOOSE Subscriber](GOOSE_SUBSCRIBER.md) | Read-only Npcap capture, ARIEC61850 GOOSE decoding, SCL/live-discovery DataSet binding, ordered `allData` leaf semantics, diagnostics, and field validation. |
| [Validation checklist](VALIDATION_CHECKLIST.md) | Discovery, reporting, monitoring, recovery, evidence, and control acceptance checks. |
| [UI validation](UI_VALIDATION.md) | Windows scaling, keyboard workflow, accessibility, multi-IED, and command-panel checks. |
| [Engine compatibility](../ENGINE_COMPATIBILITY.md) | Required ARIEC61850 source contracts and project-reference layout. |

## Evidence and project workflows

| Document | Purpose |
|---|---|
| [IO List FAT Evidence Testing](IO_LIST_FAT_EVIDENCE.md) | Dedicated one-IED FAT workspace, exact `TestPointId` mapping, ordered transition evidence, durable journal, `.xlsx`, native `.pdf`, and `.arsas` output. |
| [Phase progress](../NEXT_PHASE_PROGRESS.md) | Historical signal-selection behavior and validation records. |
| [Connection diagnostic audit](../CONNECTION_DIAGNOSTIC_AUDIT.md) | Example route and connection-failure reasoning. |
| [Changelog](../CHANGELOG.md) | Public application, documentation, website, and release history. |

## Control engineering

| Document | Purpose |
|---|---|
| [ARIEC61850 Smart Control integration](../ARIEC61850_SMART_CONTROL_INTEGRATION.md) | Application-to-engine control service integration. |
| [Smart Control feedback audit](../SMART_CONTROL_FEEDBACK_AUDIT.md) | Control completion, feedback mapping, and evidence boundaries. |
| [Close feedback event verification](close-feedback-event-verification.md) | Event-driven feedback confirmation workflow. |

## Licensing and provenance

| Document | Purpose |
|---|---|
| [Licensing model](LICENSING.md) | GPL community edition, historical boundary, and separate commercial licensing path. |
| [License and provenance audit](LICENSE_AUDIT_2026-07-14.md) | Repository-evidence review and remaining manual checks. |
| [Clean-room and interoperability policy](CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md) | Independent-development, test-fixture, UI, and external-material boundaries. |
| [External IP and provenance review](EXTERNAL_IP_AND_PROVENANCE_REVIEW_2026-07-14.md) | Repository evidence concerning external implementation and proprietary-asset contamination. |

## Project policies

- [Contributing](../CONTRIBUTING.md)
- [Community conduct](../CODE_OF_CONDUCT.md)
- [Security](../SECURITY.md)
- [Support](../SUPPORT.md)
- [License](../LICENSE)
- [Commercial licensing](../COMMERCIAL-LICENSE.md)
- [Trademark and branding](../TRADEMARK.md)

## Documentation principles

Public documentation should:

- distinguish configured SCL context from the live MMS model;
- distinguish logical source `ReportControl` identity from concrete live RCB client/runtime instances;
- distinguish exact native COMTRADE analysis from presentation-only interpolation;
- distinguish protocol readiness from switching authority and operational safety;
- distinguish current `main` behavior from the latest actually published stable installer and portable binary;
- state whether evidence comes from unit tests, deterministic fixtures, loopback, simulator, laboratory IEDs, or field use;
- avoid universal interoperability, formal conformance, document-signature, or acceptance claims;
- use synthetic or contributor-owned examples;
- exclude confidential customer, employer, station, credential, signal-list, and project material.
