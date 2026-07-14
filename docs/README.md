# ArIED 61850 Documentation

This directory contains the engineering, validation, licensing, provenance, and operating-boundary documents for the ArIED 61850 Windows application.

## Start here

| Document | Purpose |
|---|---|
| [Project README](../README.md) | Product overview, feature summary, quick start, build instructions, and public claim boundary. |
| [Architecture](ARCHITECTURE.md) | Multi-IED ownership, model identity, report-first acquisition, runtime scaling, and timestamp semantics. |
| [Validation checklist](VALIDATION_CHECKLIST.md) | Build, simulator, reporting, monitoring, control, and live-device acceptance checks. |
| [Engine compatibility](../ENGINE_COMPATIBILITY.md) | Required ARIEC61850 source contracts and project-reference layout. |

## Control engineering

| Document | Purpose |
|---|---|
| [ARIEC61850 Smart Control integration](../ARIEC61850_SMART_CONTROL_INTEGRATION.md) | Application-to-engine control service integration. |
| [Smart Control feedback audit](../SMART_CONTROL_FEEDBACK_AUDIT.md) | Control completion, feedback mapping, and evidence boundaries. |
| [Control UI verification](CONTROL_UI_VERIFICATION.md) | Command-panel interaction and display checks. |
| [Close feedback event verification](close-feedback-event-verification.md) | Event-driven feedback confirmation workflow. |

## Application workflow and progress records

| Document | Purpose |
|---|---|
| [Phase progress](../NEXT_PHASE_PROGRESS.md) | Detailed signal-selection behavior and validation record. |
| [Connection diagnostic audit](../CONNECTION_DIAGNOSTIC_AUDIT.md) | Example route and connection-failure reasoning. |
| [Implementation audit](../IMPLEMENTATION_AUDIT.md) | Application implementation review. |

## Licensing and provenance

| Document | Purpose |
|---|---|
| [Licensing model](LICENSING.md) | GPL community edition, historical boundary, and separate commercial licensing path. |
| [License and provenance audit](LICENSE_AUDIT_2026-07-14.md) | Repository-evidence review and remaining manual checks. |
| [Clean-room and interoperability policy](CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md) | Independent-development, test-fixture, UI, and external-material boundaries. |
| [External IP and provenance review](EXTERNAL_IP_AND_PROVENANCE_REVIEW_2026-07-14.md) | Repository evidence concerning external implementation and proprietary-asset contamination. |

## Project policies

- [Contributing](../CONTRIBUTING.md)
- [Security](../SECURITY.md)
- [Support](../SUPPORT.md)
- [License](../LICENSE)
- [Commercial licensing](../COMMERCIAL-LICENSE.md)
- [Trademark and branding](../TRADEMARK.md)

## Documentation principles

Public documentation should:

- distinguish configured SCL context from the live MMS model;
- distinguish protocol readiness from switching authority and operational safety;
- state whether evidence comes from unit tests, loopback, simulator, laboratory IEDs, or field use;
- avoid universal interoperability or conformance claims;
- use synthetic or contributor-owned examples;
- exclude confidential customer, employer, station, credential, and project material.
