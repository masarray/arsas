# Changelog

Notable public changes to ARSAS are recorded here. Application releases must identify the exact ARIEC61850 engine commit used for build, tests, packaging, and release evidence.

## Unreleased

## 1.6.47 — 2026-11-22 — Public stable release candidate (Windows x64)

- **P9 SG7 reporting restored:** use pinned ARIEC61850 `24077e48` (#792) instead of the earlier packaged `f75e323`; user-workstation evidence on the exact #793 merge-test tree confirms 77/77 actual Report-backed values and 4/4 independently routed configured BRCBs, with zero cyclic MMS process polling.
- **Startup/Stop reliability:** per-IED full Play operation lease prevents duplicate native Start during fast/cached Connect→Monitoring and lets Stop preempt stale callbacks.
- **SOE repeat protection:** exact timestamp+value 32-identity per-point guard deduplicates duplicate cross-BRCB physical edges but preserves distinct, historical, integrity/GI and unknown-time events, original Report frames and diagnostics.
- **IEC 61850 control audit:** Event Logs records a typed terminal operator-control result (confirmed, accepted, rejected, not-sent or unverified), true native wire/feedback evidence and distinct client command clock. No fake command Quality or IED process timestamp; existing CSV/details gain scoped control evidence.
- **Field and CI boundary:** the exact #793 combined preview had two user-operated Close/Open control confirmations with positive CommandTermination and verified feedback; physical external hardware vs local simulation not independently proven. Four BufOvfl advertisements and seven SqNum discontinuities remain visible; lossless SOE, clock quality and universal IED interoperability are NOT claimed.
- **Release integrity:** historical R10/SCL authority is preserved by strict CI reconciliation #794; public stable release requires exact-main Windows Build, ARIEC/ArdIrec regression, SCL/interoperability/RCB gates, installer + portable smoke, SHA-256, SBOM and provenance. Smart Updater/landing data must follow the published release, never a preview.

[Reporting and Play PR #792](https://github.com/masarray/arsas/pull/792) · [SOE/Control PR #793](https://github.com/masarray/arsas/pull/793) · [Physical proof gate PR #794](https://github.com/masarray/arsas/pull/794) · [Release notes](docs/RELEASE_v1.6.47.md)

## 1.6.46 — 2026-11-21 — Stable release candidate (Windows x64)

- **Smart Updater first-download reliability:** fixed a Windows-exclusive `.part` handle that was still open when installer SHA-256 verification attempted to read it. The download stream now closes before independent size/hash verification and final package rename; rejected packages never become installable.
- **Updater regression coverage:** tests now exercise a real first-time 1.1 MB simulated HTTPS download, verified cache reuse without another fetch, and wrong-SHA rejection with removal of incomplete `.part` files.
- **Preserved existing smart updater behavior:** 15-second post-startup idle delay, hourly background review with 12-hour network throttle, verified installer prefetch, modeless user notification and explicit consent before installation. Stable/RC updater cache separation and deferred reminders remain in place.
- **Release scope:** no changes to IEC 61850 reporting, IED commands, RCB/PurgeBuf behavior, ARIEC61850 or ArdIrec engine pins, or manually installed Windows code-signing status. Installer and portable packages must independently pass exact-source Windows release validation before public publication.

[Updater correction PR #789](https://github.com/masarray/arsas/pull/789) · [Publication authority](https://github.com/masarray/arsas/actions/workflows/release-windows.yml) · [Release notes](docs/RELEASE_v1.6.46.md)

## 1.6.45 — 2026-11-21 — Public Stable (Windows x64)

- **Event Logs:** Excel-like extended **full-row** selection with drag and Shift+click, clear selected-row feedback, right-click Copy, Ctrl+C, and bounded selected-row TSV export. Prevent event-detail drawer reflow from disrupting multi-row selection and context menus.
- **IED Browser:** right-click **Copy value**, **Copy field / object name**, **Copy name and value**, or **Copy visible properties** for Report ID, RCB name, Control Block reference, DataSet and other observed fields. Copy targets the exact clicked visible row, not stale selection or recycled content. Compact RCB object rows copy the displayed name, not a status token.
- **Universal engineering grid Copy:** preserve working Value Viewer, Device Explorer, Command Grid, and editable grid clipboard behavior added in #782; existing Read/Write/Control and engineering object selection semantics remain unchanged.
- **Validation:** Windows full application Build/CTest/packaging CI and user-provided on-workstation mouse/keyboard acceptance for #783 and #784. The existing ARIEC61850 and ArdIrec engine locks are unchanged; no additional claim of physical multi-vendor R7/R10 interoperability, IEC conformance, lossless SOE, or commercial Authenticode signing.

[Event Log PR #783](https://github.com/masarray/arsas/pull/783) · [IED Browser PR #784](https://github.com/masarray/arsas/pull/784) · [Release channel](https://github.com/masarray/arsas/releases)

## 1.6.44 — 2026-11-21 — Public Stable (Windows x64)

[Published release](https://github.com/masarray/arsas/releases/tag/v1.6.44) · [Field-scoped notes](docs/RELEASE_v1.6.44.md) · source commit `e5dda6a2e0ffc4e4581cdab8d917bea9861a3b8c` · pinned ARIEC61850 engine `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`.

- **Buffered report history:** decode MMS six-byte BinaryTime and use original BRCB EntryID and reason to distinguish verified `Buffer event` delivery from `Data change` and `Integrity` in the Event Log.
- **Reconnect safety:** prevent overlapping monitor starts, stale duplicated rows and repeated classification of already accepted EntryIDs; preserve independent per-RCB continuity evidence.
- **History-preserving RCB handling:** no automatic PurgeBuf or forced takeover of occupied RCBs. A destructive manual PurgeBuf request requires the explicit guarded IED Browser workflow.
- **Field observation:** SG7_BCU reported 77/77 live values, four reporting BRCBs and 337 decoded frames; BrcbA01/BrcbB01 historical buffer labeling was observed. Four BufOvfl indications and 28 SqNum discontinuities remain unresolved continuity observations, **not confirmed missing-event counts**.
- **Package:** exact-tag Windows installer, portable single EXE, SHA-256 checksums, SPDX SBOM and provenance. The public application release is not IEC 61850 certification; R7/R10 physical acceptance for the pinned trial engine remains independent.

## 1.6.43 — 2026-11-21 — Previous Public Stable (Windows x64)

[Published release](https://github.com/masarray/arsas/releases/tag/v1.6.43) · [Release notes](docs/RELEASE_v1.6.43.md). Delivered 77/77 SG7_BCU static report-backed values and 4/4 BRCB ingress in field observations, safer first-connect recovery, Smart Updater and verified Windows installation. At this version, historical SOE replay was **not** reliably labeled as buffer delivery; v1.6.44 addresses that specific gap. R7/R10 physical qualification remained incomplete.

## 1.6.42 — 2026-10-25 — Public Stable (Windows x64)

Operator-accepted application release, [published on GitHub](https://github.com/masarray/arsas/releases/tag/v1.6.42) with source commit `3db2ca98d144ab4b456e8dc1db9af18ad7a948f2`. The existing preview installer/portable assets were promoted to the stable channel **without rebuilding or changing their bytes**. Tested release workflow: [#40151975055](https://github.com/masarray/arsas/actions/runs/40151975055).

### Added and improved

- Complete IED Browser navigation (Reports, DataSets, Data Model, Files, Setting Groups, GOOSE), selected IED identity and per-IED state retention.
- Typed `ctlModel` Write/readback and immediate independent CSWI post-Write inspection; fail-closed SBO Enhanced command handling.
- Report-first multi-IED monitoring with per-device quality, timestamp and RCB ingress; optimized bounded Windows GUI updates.
- SV/SMV waveform viewer and passive first-received SV discovery without SCL/SVCB; ARStack M3.H untagged/VLAN-zero regression coverage.
- Updated GOOSE timeline, SNTP IPv4 display, Smart IP window and cool-blue interface; pinned in-process ArdIrec COMTRADE native bridge.
- Installer, portable, checksums, SPDX SBOM, provenance and signed attestations; 2,987/2,987 application tests passed in public release workflow.

### Operator evidence and remaining qualification

- Observed 5/5 monitoring endpoints, 183/183 initially displayed values and 14/14 static RCB ingress routes; targeted SG7_BCU and GR_X_7SX85 Write/readback and SBO Enhanced controls completed.
- Later 25 October capture reported 432 frames, 38 observational continuity findings and 9 IED-advertised `BufOvfl` indications. This does **not** prove 38 lost events or establish lossless buffered SOE.
- Engine `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` remains a code-trial pin (ARIEC61850 PR #192); independent physical R7/R10, replay, SCL parity and device-wide acceptance remain open. Some endpoints may be simulators. Stable is the operator-accepted application release, **not** IEC 61850 certification.
- The exact shipped code is available under [tag v1.6.42](https://github.com/masarray/arsas/tree/v1.6.42); mainline application source promotion remains separately governed in [PR #729](https://github.com/masarray/arsas/pull/729).

## 1.6.41 — 2026-10-13 — Previous stable

- Versioned native Windows title bar and clearer Event Log categories with read-only Event Properties drawer.
- Original RCB Export Filter remained the production navigation path in this release; the expanded IED Browser arrived in v1.6.42.
- [Immutable historical installer/portable and release evidence](https://github.com/masarray/arsas/releases/tag/v1.6.41).

## 1.6.37 — 2026-09-16

### Added

- Legacy SAS RCB selection presents concrete live instances such as `Buffer01`, `Buffer02`, `Unbuffer01`, and `Unbuffer02` when the connected IED actually exposes them, without adding duplicate logical placeholder rows.
- The in-process COMTRADE workstation provides native cursor, Phasor, Harmonics, and six-loop distance Locus analysis through the pinned ArdIrec bridge.

### Changed

- Source-backed IID/SCL export preserves the canonical logical `ReportControl` identity and `RptEnabled` indexing metadata instead of copying concrete runtime client-slot names into the saved engineering model.
- RCB occupancy uses a square-only indicator: yellow when a client is using the RCB and green otherwise, while detailed engineering evidence remains available separately.
- COMTRADE Phasor, Harmonics, and distance Locus presentation eases toward the latest exact native analysis target at display cadence with bounded settling; raw data and authoritative analysis values remain exact.
- Distance Locus presentation resets transient smoothing across PRI/SEC engineering-basis changes so unrelated unit bases are never interpolated together.

### Fixed

- Declarative `RptEnabled@max` metadata can no longer become authority for inventing runtime RCB instance names during source-backed export.
- COMTRADE presentation smoothing remains latest-target only and does not add a second analysis queue or periodic background work after settling.

## 1.6.33 — 2026-09-02

### Added

- FAT workspaces can import additional CID/SCD/ICD/IID sources without replacing the current project, including while another IED evidence session remains active.
- Engineering and FAT workspaces now share imported SCL signal selection, so operator selection remains consistent when switching modes.

### Fixed

- Direct-SCL FAT connection now reuses the imported model and returns after the MMS association and bounded point setup instead of rediscovering or waiting through the legacy settle path.
- Structured THD current/voltage aggregates and demand-energy values retain exact parent and phase identities and use authoritative MMS projections instead of remaining `Unknown`.
- FAT live updates are coalesced below operator input priority; steady telemetry no longer refreshes the whole collection or schedules autosave for every sample, restoring smooth scrolling and reliable Capture clicks.
- The Add IED action is independent from the active evidence-session edit lock while preserving the running IED's immutable capture mapping.
- Windows receives a stable ARSAS AppUserModelID in addition to the embedded executable icon, fixing generic taskbar grouping for portable and `dotnet`-hosted launches.

## 1.6.31 — 2026-08-15

### Fixed

- Smart reconnect now rebuilds report acquisition through the same ARIEC-authoritative hybrid planner used by initial monitoring, preserving fresh static/dynamic BRCB/URCB assignments instead of silently reverting to legacy planning.
- Reconnect clears report-traffic, change-verification, command-correlation, and acquisition-label state from the previous MMS association before new plans start, preventing false physical evidence after GI/integrity updates.
- Hybrid physical-validation snapshots and monitor updates are synchronized, preventing collection-modified exceptions and torn evidence while live report frames are being processed.

## 1.6.30 — 2026-08-15

### Added

- ARIEC-authoritative hybrid acquisition planning and execution for static BRCB/URCB, verified-free dynamic RCB slots, and per-signal MMS polling residuals.
- Physical validation tracking that distinguishes successful RCB setup from received report frames and verified process-value changes.

### Fixed

- Live discovery now requests and retains DataSet directories, so static DataSet members are present in the signal catalog and signal inventory.
- Hybrid catalog matching now selects the owning primary-value descriptor instead of treating its related quality and timestamp descriptors as ambiguous duplicates.
- Report-planning inventory clones preserve live `DatSet`, owner, enable, and reservation probe evidence, allowing verified-empty dynamic slots to remain eligible.
- Instantaneous magnitude references such as `Hz.instMag.f` now resolve quality and timestamp companions at `Hz.q` and `Hz.t`; friendly signal names no longer corrupt `instMag` into `inStatusue`.

## 1.6.20 — 2026-08-04

### Added

- The Windows portable package is now one self-contained `ARSAS-Windows-x64-Portable.exe` that starts without an installed .NET runtime and without ARSAS requesting UAC elevation.
- Release CI enforces a true one-file publish, loads the ARIEC61850 and managed Npcap dependencies from the bundle, verifies immutable engine provenance, and tests user temporary-directory access before publication.

### Fixed

- IED Explorer and IO List FAT use the shared protection-relay fascia consistently, place LIVE/STOP below the relay artwork, and avoid an icon-level status glow.
- UI contract coverage verifies the shared relay artwork, status-label placement, and absence of the former calculator-style icon.
- Visual QA documentation uses repository-auditable evidence instead of machine-local paths.

### Changed

- The installer continues to use the separately validated multi-file source, while the portable public asset is now a single EXE.
- Release checksums, SPDX SBOM, provenance, attestation inputs, download documentation, and public package metadata now identify the portable EXE instead of the legacy ZIP.
- The portable executable remains subject to AppLocker, WDAC, SmartScreen, antivirus, download-zone, and corporate execution policy. Raw-Ethernet GOOSE and SMV still require an administrator-installed and approved Npcap driver.

## 1.6.19 — 2026-08-01

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
