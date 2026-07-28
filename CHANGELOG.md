# Changelog

Notable public changes to ARSAS are recorded here. Application releases must identify the exact ARIEC61850 engine commit used for build, tests, packaging, and release evidence.

## 1.6.19 — Development

### Fixed

- Sampled Values publisher restarts are treated as counter-continuity anomalies and can no longer be presented as a clean two-cycle proof.
- The selected SV stream is locked while capture is active, and a completed snapshot is discarded if its immutable stream identity no longer matches the operator selection.
- SV continuity evidence now reports restart, gap, missing-sample, duplicate, and out-of-order counts explicitly.
- CI no longer depends on the temporary `agent/sv-core-unification` engine branch. It resolves and checks out the immutable merged engine commit from `engines/ARIEC61850.lock.json`.

### Added

- `ARSAS.Tests`, the first application-layer regression-test project.
- Regression coverage for clean and incomplete SV windows, gap/duplicate/out-of-order/restart handling, continuity evidence, and immutable stream-selection identity.
- Canonical `Directory.Build.props` version metadata aligned with the project, `VERSION`, packaging, and CI.
- Test-result artifacts in the Windows build workflow.
- A single auditable SV Evidence Bundle export containing the rendered waveform PNG, raw-sample CSV, structured manifest JSON, parser/continuity diagnostics, per-entry SHA-256 integrity file, and application/engine provenance.
- Regression coverage that opens the generated ZIP and validates its required evidence files, verdict, provenance, raw samples, diagnostics, and checksum listing.
- A schema-versioned FAT/SAT Test & Evidence Workspace with editable IEC 61850 test cases, expected and actual outcomes, operator/witness context, deviations, execution timestamps, and evidence attachments.
- A default bounded IEC 61850 FAT/SAT plan covering identity, MMS discovery, reporting/recovery, GOOSE, Sampled Values, guarded control, file transfer, SCL comparison, and closeout.
- Atomic `*.arsas-fat.json` save/open and portable audit-package export containing `workspace.json`, `report.md`, immutable evidence files, SHA-256 checksums, and package provenance.
- Regression coverage for workspace round-trip, schema rejection, complete audit-package contents, source-path redaction, and rejection of evidence changed after attachment.

### Changed

- Windows CI restores and builds the complete solution, runs application regression tests, and only then publishes the portable package.
- The SMV Snapshot Viewer enables evidence export only after a snapshot is accepted and keeps the export disabled during active capture.
- ARSAS exposes the FAT/SAT workspace from the main header without coupling it to monitoring runtime state.
- Development version advanced to `1.6.19`. The currently published stable release remains `1.6.18` until a separately validated and tagged release is produced.

## 1.6.18

### Added

- Reliable fault-record transfer across adaptive remote paths and segmented MMS responses.
- Physical IED identity handling independent from complete MMS Logical Device domain names.
- Selected-RCB Edition 1 and Edition 2 CID export with exact live RCB names.
- Passive two-cycle Sampled Values snapshot preview on `main` after the original 1.6.18 release.

### Changed

- Compact evidence-driven fault-record and RCB export workflows.
- Product positioning standardized on ARSAS as an IEC 61850 engineering workstation.

## 1.6.5

### Added

- Native Smart Control integration through `Iec61850ControlService`.
- Typed Direct and Select-Before-Operate workflows for supported DPC, SPC, INC/ISC, BSC, and APC objects.
- Per-signal Test, interlock, and synchrocheck flags.
- Two-step confirmation for Open and Close dispatch.
- Command evidence for control model, service result, CommandTermination, application error, timing, and process feedback.

### Changed

- Removed the application-level generic MMS control-write fallback.
- Cached control-object sessions per live IED association.
- Improved semantic Open/Closed handling when command and feedback wire representations differ.

## Historical licensing boundary

Revisions through `0df1007d9538b978edba67218136bc5c4f8019ad` remain available under their original terms on branch `archive/apache-2.0-final`. Current `main` and current community release packages are GPL-3.0-or-later only. See `docs/LICENSING.md`.
