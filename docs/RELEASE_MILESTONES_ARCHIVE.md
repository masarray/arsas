# ARSAS Historical Release Milestones

This document preserves the detailed historical sections formerly in the primary README. All statements are version-scoped, not current v1.6.42 behavior. For exact packages and changes, see [CHANGELOG](../CHANGELOG.md) and [GitHub releases](https://github.com/masarray/arsas/releases).

## Previous stable milestone: v1.6.41

- Versioned native title bar, readable Event Log categories and a read-only Event Properties drawer.
- The earlier v1.6.41 RCB Export Filter remained the default, while the redesigned Browser was still being developed. This description is **historical**, not the v1.6.42 feature set.
- [v1.6.41 release and its exact source](https://github.com/masarray/arsas/releases/tag/v1.6.41) remain available for comparison.

## Previous stable milestone: v1.6.40

- **Efficient Smart Discovery is the production path** — release builds fail closed if they drift back to the legacy discovery route. Discovery is association-scoped and single-flight, reuses authoritative model evidence, and keeps network work bounded.
- **Physical reporting proof** — the pinned engine/consumer combination was qualified against a real IED with actual `InformationReport` traffic and all 58 selected runtime points report-backed.
- **Persistent multi-IED shell** — the IED Explorer keeps device-specific association, live model, monitoring, event, file, control, FAT and diagnostic context immediately accessible.
- **Operator-first live workflow** — searchable live values, Sequence of Events, Alarm Annunciator and the Command Dock share the selected IED without duplicating protocol state.
- **Native FAT workspace and report** — capture V1/V2 evidence, preserve timestamps and acquisition source, review COMTRADE/time-sync evidence, and generate the same immutable layout in Preview and PDF.
- **Faster engineering exchange** — inspect concrete RCB availability, select only the required blocks, and export clean IEC 61850 Edition 2 or Edition 1 SCL from authoritative discovery/source evidence.
- **Fault records to analysis** — scan and download COMTRADE records, then open them in-process for exact time signals, RMS, Phasor, Harmonics, six-loop distance Locus and digital protection timeline analysis.
- **Dense-model responsiveness** — virtualized tables, coalesced UI updates, bounded background work and latest-value presentation keep large signal catalogs responsive. The recorded Smart Discovery target contains 4,925 semantic leaves; ARSAS does not publish an arbitrary universal device-count limit.

## Earlier milestone: v1.6.37

- **Concrete live RCB instances** — Legacy SAS RCB selection presents concrete instances such as `Buffer01`, `Buffer02`, `Unbuffer01`, and `Unbuffer02` when the connected IED actually exposes them, without adding duplicate logical placeholder rows.
- **Canonical source-backed SCL export** — an opened engineering source preserves the logical `ReportControl` identity and `RptEnabled` indexing metadata; declarative `RptEnabled@max` is never used to invent runtime instance names.
- **Clear RCB occupancy** — the compact occupancy indicator is yellow when a client is using the RCB and green otherwise, while engineering detail stays available without alarm-like captions.
- **Smooth COMTRADE Phasor and Harmonics** — presentation follows the latest exact native ArdIrec analysis target at display cadence and converges exactly after a bounded settle interval.
- **Smooth distance Locus interaction** — Locus follows the same latest-target principle and resets transient presentation state across PRI/SEC basis changes so incompatible engineering bases are never interpolated together.
- **Native authority preserved** — cursor identity, recorded samples, raw values, analysis targets, and exported evidence remain exact; easing is presentation-only and does not create a second analysis queue.

## What changed in v1.6.20

- **Real portable single EXE** — copy and run one `ARSAS-Windows-x64-Portable.exe`; no installed .NET runtime and no application-requested UAC elevation are required.
- **Portable runtime gate** — CI proves that the bundle contains exactly one distributed file, starts successfully, loads ARIEC61850 plus the managed Npcap stack, reads immutable engine provenance, and can use the current user's temporary directory.
- **Installer preserved** — the normal installer is still built from the separately validated multi-file self-contained publish and passes silent current-user install/uninstall testing.
- **Relay fascia consistency** — IED Explorer and IO List FAT share the industrial protection-relay artwork, with LIVE/STOP placed below it and no misleading icon glow.
- **Release trust evidence** — the installer and portable EXE receive public SHA-256 values, SPDX SBOM, provenance metadata, and GitHub artifact attestations.
- **Honest locked-PC boundary** — portable packaging does not bypass AppLocker, WDAC, SmartScreen, antivirus, or corporate policy; GOOSE and SMV still require an approved administrator-installed Npcap driver.

## What changed in v1.6.19

- **Rev.3 IO List import** preserves the exact IEC 61850/event-log reference, source state semantics, document control, report display reference, evidence fields, and review reasons without inventing missing telegrams.
- **Executive FAT evidence report** adds document number, revision, `AS TESTED` issue state, exact event-log reference, expected state, ON/OFF relay timestamps, result summary, test basis, and sign-off areas.
- **Fast workspace switching** keeps a loaded IO List FAT project in memory. Moving between Engineering and IO List FAT uses hide/show; the user only selects another workbook or `.arsas` project when intentionally replacing the loaded project.
- **One-click GOOSE workflow** opens the GOOSE Subscriber from an IED card, resolves the routed Ethernet adapter when unique, and starts receive-only capture.
- **One-click SMV workflow** opens the Sampled Values workspace, selects the first discovered stream, starts a bounded two-cycle capture on the routed adapter, and renders values plus raw waveform evidence.
- **Real preparation progress** follows actual connection, discovery, binding, and acquisition readiness with smoothed determinate progress instead of an infinite animation.
- **Evidence continuity and lifecycle hardening** protect completed FAT rows during continuation, preserve loaded workspace state, and avoid window-show races during application shutdown.

