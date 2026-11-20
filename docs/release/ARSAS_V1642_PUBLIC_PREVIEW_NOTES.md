# ARSAS v1.6.42 — Public Preview (Windows x64)

**Preview release — not final stable, IEC 61850 conformance certification, or a claim of lossless SOE.**
ARSAS v1.6.41 remains the last qualified public stable release. v1.6.42 is intended for engineering evaluation, multi-IED FAT, interoperability verification, and operator feedback.

## Included in this preview

- **IED Browser and device selection:** Reports → DataSets → Data Model → Files → Setting Groups → GOOSE, selected IED name/IP emphasis, model-backed object details, scoped Read and typed Write, verified readback, and fail-closed command admission.
- **Report-first monitoring / SSOT:** static DataSet membership, per-IED signal values/quality/timestamps, event source identity, responsive multi-IED workbench and bounded UI updates; no cyclic process-value MMS polling as a substitute for report ingress.
- **Modern Windows UI:** cohesive cool-blue controls and dialogs, compact Smart IP form, improved per-IED Browser state retention, responsive GOOSE timeline, visible SNTP IPv4 display, and adapted startup window sizing.
- **IEC 61850 control:** exact live `ctlModel` selection/Write/readback, immediate focused CSWI reinspection instead of an indefinitely disabled “Reading…” state; typed SBO Enhanced command handling with association and safety fences.
- **SV/SMV:** accepted waveform user interface plus passive first-received SV discovery when SCL/SVCB data are unavailable; retains ARStack M3.H frame/untagged/VLAN-zero interoperability regressions.
- **Native engineering tools:** COMTRADE viewer with the SHA-pinned ArdIrec bridge and applicable test coverage.

## Observed operator FAT scope — evidence, not certification

A five-endpoint diagnostic captured on 2026-10-25 reported **5/5 monitoring sessions**, **183/183 initial values displayed**, and **14/14 static RCB routes receiving ingress**. Direct targeted commissioning showed SG7_BCU and GR_X_7SX85 exact CF `ctlModel` Write/readback, post-Write typed inspection and positive termination for the tested SBO Enhanced operations.

**Known limitations, not waived:** 427 observed report frames included 36 continuity findings and 9 IED-advertised `BufOvfl` occurrences. These counts do not establish 36 lost events but also do not support lossless buffered SOE claims. Further physical R7/R10 testing, reconnect/replay, event chronology, SV wire parity, target-specific command edge cases and independent equipment verification are still required. Some endpoints in the diagnostic could be same-host simulators. No general vendor compatibility certification is implied.

The ARIEC61850 engine commit is pinned to the trial revision recorded in the bundled provenance and the application's `engines/ARIEC61850.lock.json`, linked to engine PR #192. Do not treat it as the historically qualified stable baseline.

## Download and verify

The release provides the **Windows x64 installer** and **portable EXE**, a SHA-256 checksums file, SPDX 2.3 SBOM and source/engine provenance. Validate the installed files against the accompanying checksums and provenance. Release assets are assembled, smoke-tested, and attested by the release workflow, independently of historical RC builds.

This release does **not** replace the v1.6.41 stable updater. Future stable promotion requires separate hardware FAT, review and a new versioned release; do not relabel this preview as stable.
