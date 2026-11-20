# ARSAS v1.6.43 — IEC 61850 protocol/client tester

## Release purpose
This is a public Windows x64 **engineering tester** for IEC 61850 MMS, report monitoring, control-service testing, SCL/IED exploration, I/O FAT, SV/GOOSE and COMTRADE diagnostics. It is not SCADA or a station-protection controller. No external device qualification can be claimed from a successful software release.

## Improvements shipped
- P8/P9 SG7_BCU static DataSet reporting: 77/77 selected runtime values and independently routed 4/4 configured BRCBs in field observations; no cyclic MMS process polling in static report-only mode.
- First-connect, exact static RCB inventory/reconciliation, two-bit status display, report diagnostics and bounded Windows discovery paths.
- Smart Updater: verified official stable package prefetch, SHA/size checks, opt-in installation and separate RC cache. No unattended restarts.
- Installer identity tests and exact-SHA sealed Windows CI package and installed smoke tests.

## Known issues — do not hide diagnostic uncertainty
- **SOE Event Log** currently does not reliably classify buffered-history replay versus live reports. `ReasonForInclusion` (GI/integrity/data-change) is not equivalent to delivery origin. The wire decoder has EntryID/TimeOfEntry/BufOvfl evidence, but the per-event drawer does not yet expose it. Track [issue #769](https://github.com/masarray/arsas/issues/769).
- SG7_BCU diagnostic on 21 Nov observed 225 report frames with 4 BufOvfl and 9 SqNum discontinuities. These are diagnostic observations, **not** proof of lost process events nor a reason to block use of the tester.
- Some vendor-specific command cancellation and discovery scenarios require continued interoperability fixes (ARIEC61850 #172, #159, #162). Tester operations should show real wire-service results and errors rather than assert universal compatibility.

## Release acceptance
A release is publishable when the immutable source SHA matches the full Windows Build regression/engine/native proof, the same canonical payload passes Windows installer install/repair/uninstall smoke, and packaged EXE/SBOM/provenance digests match. Outstanding vendor-specific field-qualification issues are documented product defects, not manual publication vetoes.
