# ARSAS Product Roadmap

ARSAS is being developed toward a complete, engineer-facing IEC 61850 workstation. The roadmap is organized by operational capability rather than marketing labels, and it distinguishes shipped behavior from engineering previews and planned work.

## Product vision

The target is one application that can support the practical IEC 61850 lifecycle from engineering input to live evidence:

```text
SCL project, IO List, or IED endpoint
               ↓
Configured and live model understanding
               ↓
MMS values, reports, GOOSE, Sampled Values, files, and control
               ↓
Validation, diagnostics, evidence, and reusable project output
```

ARSAS is the application and workflow layer. Protocol implementation belongs in [ARIEC61850](https://github.com/masarray/ARIEC61850) so it can remain independently testable and reusable.

## Status definitions

- **Available** — implemented in the current mainline application and expected to remain compatible within the documented boundaries.
- **Engineering preview** — implemented enough for controlled evaluation, but validation depth, performance, interoperability, or UX is still evolving.
- **Planned** — accepted product direction without a guaranteed release date.
- **Research** — technical feasibility, interoperability, and architecture are still being evaluated.

## Current foundation

### Available

- Multi-IED MMS associations and live model discovery.
- Report-first live monitoring with bounded polling fallback.
- DataSet and RCB inspection, static/dynamic report planning, SOE, and diagnostics.
- Read-only GOOSE subscription with ordered data and model binding.
- MMS file-service browsing and bounded fault-record download.
- Guarded Direct and Select-Before-Operate control workflows.
- SCL import, endpoint extraction, configured/live context, and export services.
- Project persistence, signal selection, cached model context, and diagnostic evidence.
- Dedicated SDI IO List FAT workspace with strict Excel import, one-IED sessions, OFF → ON → OFF evidence, quality-aware results, reconnect protection, durable evidence journals, Excel result copies, native PDF reports, and portable `.arsas` continuation projects.

### Engineering preview

- Sampled Values / SMV stream viewer and per-IED entry points.
- Broader process-bus performance and sequence-quality supervision.
- More complete fault-record classification and transfer workflow automation.

## Milestone 1 — Complete file-transfer engineering

**Goal:** make fault-record retrieval reliable across diverse IED file-service layouts.

- Recursive and bounded directory browsing.
- Capability discovery and transparent negative responses.
- Download progress, cancellation, timeout, and retry boundaries.
- COMTRADE set recognition (`.cfg`, `.dat`, `.hdr`, `.inf`, vendor companions).
- Safe local naming and duplicate handling.
- Transfer evidence export and sanitized diagnostics.
- Automated engine tests for directory, open, read, close, and failure paths.

**Definition of done:** fault records can be retrieved from representative multi-vendor test devices without vendor-specific logic in the ARSAS UI layer.

## Milestone 2 — Production-grade Sampled Values

**Goal:** turn the current SMV viewer direction into a validated process-bus engineering tool.

- IEC 61850-9-2 / IEC 61869-9 stream discovery and decoding boundaries.
- APPID, VLAN, destination MAC, SVID, `smpCnt`, `confRev`, `smpSynch`, and sample-rate evidence.
- SCL binding to sampled-value control blocks and DataSets.
- Channel naming, engineering units, scaling, quality, and phase grouping.
- Loss, duplication, sequence, timing, and synchronization diagnostics.
- Efficient rolling visualization without per-sample UI allocation.
- Exportable capture summaries and validation evidence.

**Definition of done:** representative streams can be decoded, bound to engineering context, monitored for quality, and visualized without compromising workstation responsiveness.

## Milestone 3 — Full SCL generation workspace

**Goal:** progress from SCL-assisted viewing/export to practical project authoring.

- Create and edit IED, AccessPoint, Server, Logical Device, and communication structures.
- DataSet authoring with validation and stable member order.
- Report, GOOSE, and Sampled Values control-block generation.
- Communication address, APPID, VLAN, MAC, and redundancy validation.
- Edition-aware import, modification, normalization, and export.
- Schema, referential-integrity, naming, and compatibility checks.
- Visual diff between configured intent, generated output, and live model.
- Reusable templates for laboratory and commissioning projects.

**Definition of done:** an engineer can create or modify a bounded SCL project, validate it, export it, reopen it, and compare it against a live IED without hidden data loss.

## Milestone 4 — Unified test and evidence workspace

**Goal:** connect protocol features into repeatable FAT, SAT, and commissioning workflows.

### Phase 1 — Available on current mainline

- Import an approved ARSAS Excel IO List for SDI / digital indications.
- Keep imported project, IED, `TestPointId`, expected state, and IEC 61850 object-reference identity explicit.
- Run one active IED test session while observing all enabled imported points for that IED.
- Require a trustworthy OFF baseline before accepting a new OFF → ON transition.
- Require the matching ON → OFF transition before PASS.
- Preserve IED timestamp, independent ARSAS capture timestamp, quality, acquisition source, event ordering, and reconnect generation.
- Autosave project progress and append hash-chained evidence journals.
- Export a result workbook without modifying the approved source workbook.
- Generate a dependency-free native PDF report.
- Move the project between laptops as a verified `.arsas` package while retaining legacy `.arsas-iofat` readability.

See [IO List FAT Evidence Testing](docs/IO_LIST_FAT_EVIDENCE.md).

### Remaining planned scope

- Analog indication tolerance and scaling campaigns.
- Output-command and feedback test plans with explicit authority gates.
- Reusable GOOSE, Sampled Values, file-transfer, control, and time-synchronization test steps.
- Cross-service time correlation between reports, SOE, GOOSE, SMV, commands, and transfers.
- Engineer review, exception management, witness approval, and formal sign-off fields.
- Multi-user approval and digital-signature models.
- Sanitized campaign-level export packages beyond the current IO FAT project format.
- Project templates for repeatable regression and customer demonstrations.

**Definition of done for the complete milestone:** the same project can be used to prepare, execute, review, approve, and export evidence for a controlled multi-service IEC 61850 test campaign.

## Milestone 5 — Interoperability and maintainability

**Goal:** keep the suite dependable as protocol coverage grows.

- Deterministic protocol fixtures and regression captures with documented provenance.
- Capability matrices by service and tested behavior—not brand marketing claims.
- Performance budgets for association, discovery, reporting, capture, transfer, and UI updates.
- Compatibility gates between ARSAS and ARIEC61850.
- Versioned project format with migrations.
- Accessibility, keyboard workflow, localization readiness, and high-DPI validation.
- Signed releases, reproducible packaging, checksums, and software bill of materials.

## Explicit non-goals

ARSAS does not claim to provide:

- automatic switching authority or proof of safe isolation;
- formal IEC 61850 conformance certification;
- universal interoperability with every implementation;
- cybersecurity approval for connection to an operational station network;
- functional-safety certification;
- automatic witness approval or legally binding digital signatures;
- unrestricted use of confidential SCL files, IO Lists, packet captures, credentials, or customer data.

## Contribution priorities

Contributions are especially valuable when they include reproducible tests, protocol fixtures with clear provenance, bounded failure behavior, diagnostic evidence, and documentation of the operational assumptions.

Do not include proprietary source code, confidential customer files, employer material, relay settings, credentials, IO Lists, or packet captures that you are not authorized to publish.
