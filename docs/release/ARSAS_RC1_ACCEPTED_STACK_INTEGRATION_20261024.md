# ARSAS RC1 accepted stack integration — 2026-10-24

**DRAFT ONLY: NOT PUBLIC STABLE, NOT MAIN.**

- Parent RC0 `77c6cd2dccd27de155bbad6799b3d914be118c9a` includes accepted SSOT V2 responsive reporting, frozen MMS Write->readback Browser SSOT, and independent final RC0 handoff.
- Final operator-accepted SMV checkpoint `84b6e15f0e7a94b419d172921b366c4cb7f28793`, PR #725, original source #722 `4b2b62f882a81988a39a2f82d1ac8ff67592be02`.
- Import 27 SMV-only source/test/document blobs. Take entire final `Views/SmvInlineViewer.xaml` without old UI rollback. Combine SMV Dock and RC0 exact DPC Boolean safety test. Keep RC0 `App.xaml` (identical to SMV), four IoList embedded Inter paths (same intended font behavior), already equivalent SMV theme test.
- Preserve SSOT v2 model/index/association/live-value journal and SG7 performance, MMS typed one-shot Write/Control admission, 77/77 SG7 value + 4/4 RCB previous field proof, ZIVAVR 8/8+1 RCB, GR_X 13/13+2 RCB, SCL/GOOSE, Smart IP, COMTRADE ArdIrec. These are source/field checkpoint proofs, NOT this combined binary's proof.
- No automatic cyclic process polling, no invented q/t/values, no unqualified SBOw/Operate, no false SV PTP-lock claim. Retain RAW-vs-9-2LE calibration truth and SMV HOLD across stream changes.
- Engine lock is inherited unchanged: `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` (ARIEC staging PR #192 still DRAFT, not qualified production).

## Mandatory gates before public stable

1. Exact-head Windows Build and application/engine regression, native bridge/CTest, R7, SV, P0/P1, IO List, COMTRADE, portable+installer smoke and SHA256 manifest. Do not substitute a historic green SHA.
2. Diff and verify final SMV approved presentation, SG7 authority, Write readback, WPF Command Dock tap-first/BSC numeric, Browser navigation order, Smart IP, UI response, failure paths and reconnect.
3. Combine with latest public main only through reviewed three-way merge preserving independent accepted main COMTRADE, release/update/security, installed version and engine lock. No force push or public auto-release.
4. Retain each historic vendor/model/firmware user FAT acceptance and perform a bounded combined-binary cross-feature FAT on SG7_BCU, ZIVAVR, GR_X, and SV publishers. Investigate report continuity/BufOvfl and validate UI p95/p99; do not demand redoing entire weeks of accepted FAT unless exact feature changed.
5. Only after all checks and operator acceptance: immutable release SHA/tag, Windows portable + installer, SHA256, source/engine manifest, signed release notes, known limits and rollback baseline.
