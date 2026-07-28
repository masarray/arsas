# Changelog

Notable public changes to ARSAS are recorded here. Application releases must identify the exact ARIEC61850 engine commit used for build, tests, packaging, and release evidence.

## 1.6.19 — Development

### Fixed

- Sampled Values publisher restarts are treated as counter-continuity anomalies and can no longer be presented as a clean two-cycle proof.
- The selected SV stream is locked while capture is active, and a completed snapshot is discarded if its immutable stream identity no longer matches the operator selection.
- SV continuity evidence now reports restart, gap, missing-sample, duplicate, and out-of-order counts explicitly.
- CI no longer depends on the temporary `agent/sv-core-unification` engine branch. It resolves and checks out the immutable merged engine commit from `engines/ARIEC61850.lock.json`.

### Added

- Dedicated **FAT / IO List Testing** entry point beside general IEC 61850 testing.
- Strict ARSAS Excel import for SDI test plans with project, IED, `TestPointId`, object-reference, and expected-state validation.
- One-IED-at-a-time FAT sessions that monitor all enabled imported signals for that IED.
- Ordered OFF → ON → OFF evidence evaluation with reconnect baseline protection, quality-aware verdicts, IED timestamps, ARSAS timestamps, acquisition source, and monotonic event ordering.
- Append-only hash-chain evidence journals, autosaved local project snapshots, and safe cross-laptop continuation.
- **Export Excel** result copies that update matching `TestPointId` rows without modifying the approved source workbook in place.
- Dependency-free native PDF 1.4 IO FAT reports ported from the project-owned ARIEC60870 PDF primitive design.
- Portable `.arsas` IO FAT projects containing the source workbook, project snapshot, verified evidence journals, Excel results, native PDF report, manifest hashes, and handover notes.
- Backward-compatible import of legacy `.arsas-iofat` filenames.
- `ARSAS.Tests`, the first application-layer regression-test project.
- Regression coverage for IO List import, live binding, transition ordering, reconnect handling, evidence integrity, Excel export, native PDF output, `.arsas` round-trip, legacy import, tamper rejection, and UI contracts.
- Regression coverage for clean and incomplete SV windows, gap/duplicate/out-of-order/restart handling, continuity evidence, immutable stream-selection identity, and deterministic evidence export.
- Canonical `Directory.Build.props` version metadata aligned with the project, `VERSION`, packaging, and CI.
- Test-result artifacts in the Windows build workflow.
- Portable Sampled Values engineering evidence bundles containing:
  - structured `manifest.json` capture identity and verdict;
  - separate application/engine `provenance.json`;
  - invariant-culture raw `samples.csv`;
  - rendered `waveform.png`;
  - explicit `diagnostics.txt` and evidence boundary;
  - deterministic `SHA256SUMS.txt` integrity records.
- Runtime publication of the immutable `ARIEC61850.lock.json` provenance used by exported evidence.

### Changed

- New IO FAT portable exports use the shorter `.arsas` extension; `.arsas-iofat` remains readable for compatibility.
- Browser Print-to-PDF is no longer the primary IO FAT report workflow. ARSAS writes the PDF directly with its built-in native engine.
- The first unified test/evidence milestone is now available for imported SDI IO List campaigns; broader reusable campaigns across GOOSE, Sampled Values, files, controls, approvals, and multi-user review remain roadmap work.
- Windows CI restores and builds the complete solution, runs application regression tests, and only then publishes the portable package.
- The SV viewer can export a reviewed evidence package after a bounded snapshot without claiming current, voltage, engineering units, calibration, formal conformance, or universal interoperability.
- Development version remains `1.6.19`. The currently published stable release remains `1.6.18` until a separately validated and tagged release is produced.

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
