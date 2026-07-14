# Changelog

Notable public changes to ArIED 61850 are recorded here. The project uses application version numbers in `ArIED61850Tester.csproj`; release tags and packages should reference the exact ARIEC61850 engine revision used for that build.

## Unreleased

### Added

- Product website for GitHub Pages with overview, feature, Smart Control, and architecture pages.
- Search-engine metadata, canonical URLs, structured software data, sitemap, robots file, web manifest, social-preview artwork, and responsive navigation.
- Automated landing-page validation for titles, descriptions, canonical URLs, internal links, JSON, XML, SVG, and image alternative text.
- Documentation hub, security policy, support guide, community conduct policy, UI validation checklist, and structured issue templates.

### Changed

- Rebuilt the public README around the product workflow, engineering capabilities, application/engine boundary, quick start, validation boundary, and support path.
- Standardized public product metadata on `Ari Sulistiono / masarray` and version `1.6.6`.
- Updated CI to build against the reviewed ARIEC61850 `main` integration baseline.
- Renamed the external-IP audit document as an evidence-based provenance review.
- Expanded source-clean verification to website, SVG, web manifest, and public wording files.

## 1.6.6 — Current development version

### Added

- Engine-owned SCL workspace integration requirement for opening common SCL file types and preparing configured-versus-live model workflows.
- CI compatibility checks for Smart Control and SCL workspace contracts.
- Portable Windows x64 packaging at version 1.6.6.

### Changed

- Open SCL now depends on the reusable ARIEC61850 workspace service rather than a separate long-term application-owned protocol parser.
- Product metadata describes SCL-assisted project setup, live MMS discovery, independent multi-IED monitoring, reporting diagnostics, events, and guarded control.

## 1.6.5

### Added

- Native Smart Control integration through `Iec61850ControlService`.
- Typed Direct and Select-Before-Operate workflows for supported DPC, SPC, INC/ISC, BSC, and APC objects.
- Per-signal Test, interlock, and synchrocheck flags.
- Two-step confirmation for Open and Close dispatch.
- Command evidence for control model, service result, CommandTermination, application error, timing, and process feedback.
- Signal-selection grid sorting, keyboard filtering, visible-row bulk selection, and virtualized large-model behavior.

### Changed

- Removed the application-level generic MMS control-write fallback.
- Cached control-object sessions per live IED association.
- Improved semantic Open/Closed handling when command and feedback wire representations differ.

## 1.6.4

### Added

- Copy Diagnostic report with application/engine version, adapter, endpoint, TCP reachability, association, discovery, and communication-journal context.

### Fixed

- Window shutdown cleanup re-entry that could cause a WPF closing-state exception.
- Connection documentation distinguishing TCP refusal and timeout from later IEC 61850 association stages.

## Historical licensing boundary

Revisions through `0df1007d9538b978edba67218136bc5c4f8019ad` remain available under their original terms on branch `archive/apache-2.0-final`. Current `main` and current community release packages are GPL-3.0-or-later only. See [docs/LICENSING.md](docs/LICENSING.md).
