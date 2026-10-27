# ARSAS ENGINEERING HANDOFF D24 → D25 — P7.6 review bugs corrected without RC/main mutation
**Date:** 2026-10-27 WIB. Continues [D23](ARSAS_RECONCILIATION_D23_2026-10-27.md).
**Scope:** Review-targeted read-only local P7.6 diagnostic qualification hardening only. No engine, native runtime, WPF GUI, MMS write/control, GOOSE, stable release, RC or main changes.

## D24 concrete engineering commit

[PR #759](https://github.com/masarray/arsas/pull/759) remains **OPEN READY FOR REVIEW, NOT MERGED** on protected integrated RC base `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`.

- Prior D23 HEAD: `fc8712c30b01480ef58cc59fe344bbb15dabb523`.
- New D24 exact HEAD: [`43b7eed442713194f76ea115ce3bf5cecdc6f5cf`](https://github.com/masarray/arsas/commit/43b7eed442713194f76ea115ce3bf5cecdc6f5cf).
- One consolidated commit, **three modified paths**: `scripts/verify-p7-dual-ingress-diagnostic.py`, `scripts/test-p7-dual-ingress-diagnostic.py`, `docs/audits/P7_6_FIELD_QUALIFICATION_INTAKE.md`. No additional PR/workflow or runner class introduced. Original PR still has four changed paths, including existing CI P0 workflow integration.

### Corrected reviewer findings

1. **Signal coverage provenance:** The local verifier previously trusted `staticBRCB/staticURCB` coverage counters independently of each target's `bindings=` field. It now requires **exact per-ingress, per-family equality** with the sum of concrete routed target bindings, mirroring native `StaticAcquisitionParityTracker.BuildEvidence`. It correctly preserves multiple binding incidences across overlapping DataSets/RCBs and does **not** compare that sum to unique `requested` IEC signal count. Four ingress/family negative subtests and a legitimate overlapping-incidence positive case were added.
2. **Oversized numeric fail-closed:** Diagnostic regex groups for the eight producer-side numeric fields now match ASCII `[0-9]{1,10}` rather than unlimited `\\d+`. Invalid numeric rows become `QualificationError` instead of raising uncaught Python `ValueError` in `int()`. The CLI can overwrite an old PASS with `NOT_ESTABLISHED`; regression fixtures exercise 5,000-digit ingress, traffic and binding fields, non-ASCII digits, no traceback, and no stale PASS/temp file.

Both Codex P2 inline findings were answered with the fix SHA and exact-SHA CI evidence and their review threads marked resolved. This is not a GitHub owner's APPROVE or branch-protection approval.

### Exact-head CI evidence (read before any acceptance claim)

- [CI P0 Workflow Integrity #40427176048](https://github.com/masarray/arsas/actions/runs/40427176048): **SUCCESS** at `43b7eed`, including read-only P7.6 step **Ran 24 tests in 0.181s / OK** (up from D23's 20). No rerun.
- [CI P1 Advisory #40427176092](https://github.com/masarray/arsas/actions/runs/40427176092): **SUCCESS** at same HEAD.
- [Canonical Windows Build #40427176051](https://github.com/masarray/arsas/actions/runs/40427176051): **IN_PROGRESS at checkpoint creation**. Do not claim finished Windows/.NET/native CTest, portable EXE smoke or fresh artifacts until exact-run completion is independently verified. No manual rerun.

### Baseline and coordination

- ARSAS main `14e5dddfbaae012b61a021366c1a864d83d316f2` unchanged.
- ARIEC61850 main `3d21238d0b62f24faecd5948a4ee238c82b35966` unchanged.
- Integrated RC #727/#729 OPEN DRAFT at `817c3910e8223fa2ac531a4426ada5d2d4bbbe50` with trial engine pin `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` unchanged.
- PR #757 CI-P4 OPEN DRAFT unchanged at `e0c9724b203ac38f2a947d71795ac1173ca5ed5b`.
- Protected safety #753–#756, Write #726/#716, SG7 #688, GUI #732 are untouched. D24 changes only three files already in D23's four-file #759 path set, whose file overlap with those PRs was zero. Semantic integration is not proven by path comparison.
- GitHub App branch-protection endpoint still returns **403 Resource not accessible by integration**. Treat owner required-check review as blocked, not absent.
- GitHub search actual open inventory: **101 PRs, 16 independent issues** at D24; no backlog cosmetics or closures performed.
- #479 stays OPEN because replacement #759 has not been integrated. #446 real IEC 61850 field interoperability, #520 buffered SOE/BufOvfl/EntryID, #542 CI cost, #660 prerelease authority remain OPEN.
- Published stable `v1.6.42` unchanged.

### Engineering and cost boundary

PR #759 P7.6 validator only judges offline Copy Diagnostic consistency. Always output `physicalVerification=false` and `wirePcapVerification=false`. P0 tests, a green Windows compile and positive syntax/semantic checks can **never** substitute independent wire PCAP and real-device R7/R10 FAT authority.

D24 intentionally used **one consolidated source push**, without rerunning D23's successful runs or creating a second build job. CI P4 #757 is separate. #542 COMTRADE independent Windows CMake/CTest cannot be deauthorized merely because one prior native DLL digest and 9 CTest names matched canonical Windows; `deduplicationAuthorized=false` remains binding.

### D25 entry criteria

1. Inspect final Windows #40427176051 conclusion and six expected artifacts, matching new exact HEAD; if a failure occurs, diagnose its specific log and make a targeted fix, not a blind rerun.
2. Confirm no new Codex/human review blocker after D24 push. Inspect required checks/branch policies with owner access; no silent bypass of 403.
3. Seek explicit owner integration approval before any #759 → integrated RC merge. If approved, use expected-head lease and retest the *new RC integration SHA*; only afterward re-evaluate historical #479.
4. Continue #542 cost engineering using actual runner-minutes and independent COMTRADE acceptance contract; preserve #757 gates until owner approved.
5. Keep RC #727/#729 draft and published stable unchanged until real physical acceptance, trial-engine authority and SCL R7/reference guard are satisfied.

**D24 milestone:** One exact-head reviewed code fix with 24/24 actual Ubuntu CI P0 tests and P1 green, review defects resolved, isolated audit handoff. Windows and owner/physical integration remain distinct uncompleted gates.
