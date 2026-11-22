# ARSAS v1.6.47 — Reliable P9 reporting, SOE and control audit

## Release scope
Windows x64 public-stable candidate integrating field-tested P9 ARIEC61850
`24077e48e393ce3dc2cd1409db6e96f27aa1e368`, the Play/Stop
single-flight correction ([#792](https://github.com/masarray/arsas/pull/792)),
and the bounded IEC 61850 Event Logs improvements
([#793](https://github.com/masarray/arsas/pull/793)).
Publication is authorized only from the exact merged `main` source after
all protected Windows build, regression, installer, release and checksum gates
complete. This draft itself neither publishes nor changes the public manifest.

## Engineering improvements
- **Four independently verified configured BRCBs:** restores the P9 reporting
  successor rather than the v1.6.46 old `f75e323` engine pin. Keep actual
  Report/RCB ingress distinct from RptEna/GI setup and from initial display
  coverage. Avoid cyclic process MMS polling, fabricated quality/timestamps,
  implicit dynamic RCB mutation and automatic GI retry.
- **Reliable Play/Stop handoff:** the per-IED Play lease spans connect,
  cached association and StartMonitoring, avoids double Start while native
  activation is still in flight, and lets Stop preempt delayed continuation.
- **SOE exact-event repeat protection:** a bounded, per-point/per-association
  32-identity guard deduplicates the same real wire data-change by exact
  process timestamp and discrete value across overlapping RCBs. An earlier
  distinct Intermediate, new timestamp/value, integrity, GI or unknown-time
  event is not silently discarded. Report frames, RCB continuity and value
  updates are still ingested before SOE event deduplication.
- **Auditable operator control:** one typed terminal outcome in the existing
  Event Logs distinguishes confirmed, accepted without feedback, rejected,
  not sent and unverified controls. Check/Test flags remain *requested*
  parameters; accepted service is not proof of breaker movement. IEC Control
  AddCause is shown only when provided. Client command time and IED process
  time remain distinct, and command Quality is truly blank (not synthetic q).
- **Event details and CSV:** operator control evidence lives beside existing
  process and RCB forensics without changing the prior process-event schema.

## User-workstation SG7_BCU acceptance (22 November 2026)
The user tested the combined PR #793 merge candidate `13284bf6706a42470807368d0e2afcf65e2f95ed`,
which shares source tree `8b36922dd0fb64b528380d6e0ba687d0d7214954` with
PR head `02dc16f0d5012cf174f333fff11151ed195987bc`, using the pinned
P9 engine. The diagnostic (SHA-256 anchored separately; raw workstation and
network identifiers not committed) reported 77/77 real Report-backed values,
0 pending, and 4/4 independently routed BRCBs with 128 frames. User-operated
XCBR1 Close then Open returned positive SBOw, Operate, CommandTermination
and independently confirmed process feedback. Command Quality was empty and
the Event Log retained historical Intermediate separately.

This is a **scoped user-workstation functional acceptance**. The reported
endpoint IP is also assigned locally; physical external hardware versus
local simulation has not been independently established. It is **not** a
universal hardware/vendor qualification.

## Known limitations and safety boundaries
- Four BRCBs advertised buffer overflow. Seven SqNum discontinuities were
  observed. These are diagnostic findings, **not certified event-loss counts**
  and **not proof of lossless buffered SOE/replay**. Do not hide or auto-dismiss.
- Full original SCD/IID parity and IED clock-quality synchronization were
  not established by this workstation report. BinaryTime TimeOfEntry must
  not be substituted for process `t` or used to invent time quality.
- No automation may send breaker/switch commands. Operator confirmation and
  IEC typed-control safety gates remain mandatory.
- The P9 engine SHA is independently pinned from the reviewed ARIEC staging lineage; [ARIEC staging→main PR #192](https://github.com/masarray/ARIEC61850/pull/192) remains **draft**. Publishing an ARSAS tester build does not merge or promote that wider 250-commit engine workstream, resolve engine issue #172, or certify unrestricted control/FAT.
- Historical R10/SCL acceptance is preserved as immutable lineage;
  [gate reconciliation #794](https://github.com/masarray/arsas/pull/794)
  is not permission to alter baseline evidence or accept arbitrary engine pins.

## Release verification
Before the release is public: exact-main Windows build and tests, pinned ARIEC
and ArdIrec bridge tests, SCL R7/interoperability/RCB guards, native portable
smoke, validated installer installation/uninstallation, checksums, SPDX SBOM,
provenance and final asset/source SHA must pass. Never reuse a preview PR
artifact under the stable tag.

## Distribution
Official GitHub Releases are the single source of truth. Setup and portable
download links and the landing-page latest version are updated only after
actual stable binaries and their SHA-256 hashes are published and verified.
Windows builds remain Authenticode-unsigned; Windows SmartScreen can warn.
Previous stable: [v1.6.46](https://github.com/masarray/arsas/releases/tag/v1.6.46).
