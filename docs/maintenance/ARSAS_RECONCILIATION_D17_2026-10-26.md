# ARSAS D17 — Six historical PRs retired with source/test evidence
**Date:** 2026-10-26 Asia/Jakarta. Continues [D16](ARSAS_RECONCILIATION_D16_2026-10-26.md) on immutable audit commit `7aa5fd69fc5c4c98f562aad3a73cb48519bd28ee`. **Not a release, engine repin, RC merge or physical FAT.**

## Protected baseline / preflight
- ARSAS main = `14e5dddfbaae012b61a021366c1a864d83d316f2`; ARIEC61850 main = `3d21238d0b62f24faecd5948a4ee238c82b35966` — unchanged.
- Protected integrated RC #727/#729 = `817c3910e8223fa2ac531a4426ada5d2d4bbbe50` — still both OPEN DRAFT; **not** in main/stable.
- Engine trial pinned in RC = `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`, not promoted into engine main.
- Safety #753 `0652f66bed0c72dded20ff547e23e2ef1989341c`, #754 `2aab054b6cf9eb5500b1dce5ef5957eb8ba0232d`, #755 `7d235554225aa0341561b4e887a852fb6fd8e269`, stacked #756 `098ec4abdf7ce8681ec97111f12f3f6151eade78` unchanged.
- Frozen Write #726, Boolean Write #716, SG7 #688 and GUI #732 remain separate-owner lanes; **no edits** made on any lane.
- Published stable `v1.6.42` unchanged.

## D17 executed on GitHub: six PRs closed as SUPERSEDED/INTEGRATED RC ancestry, not merged again

Every PR was checked at the **exact unchanged PR HEAD** against RC; each `behind_by=0` (true ancestor of RC). Verified all original changed paths still exist in RC, original test blob SHA, and preservation of operator/safety semantics in **evolved** source. Commented full disposition **before** closing as unmerged.

| PR (comment permalink) | Original HEAD | Paths | Original test blob | Code/behavior reconciliation |
|---|---|---:|---|---|
| [#471](https://github.com/masarray/arsas/pull/471#issuecomment-6418596841) P7.3 static ingress parity | `389a6353756cb30f5f6550e4649a00391dc89242` | 7 | identical `StaticAcquisitionIngressParityTests.cs` (7 tests, 37 assertions) | `StaticAcquisitionParityTracker` and related model/diagnostic still preserve ingress/FCDA/member-order/fingerprint authority, exact RCB slots excluded from semantic parity, missing/ambiguous evidence incomparable |
| [#484](https://github.com/masarray/arsas/pull/484#issuecomment-6418601538) P7.6C continuity | `397d7836e5b071d4f3c6b375f9c51879c58eeb48` | 3 | identical `ReportContinuityP76CTests.cs` (12 tests, 43 assertions) | Per-association exact-RCB `ReportStreams`, SqNum/SubSqNum 16-bit rollover, segmentation, BufOvfl and opaque EntryID evidence remain observational; no introduced polling/mutation |
| [#485](https://github.com/masarray/arsas/pull/485#issuecomment-6418605756) P7.6C bounded Copy Diagnostic | `8d764128ccf764b20d27d178dfec64ec71581b81` | 5 | identical `ReportContinuitySnapshotP76CTests.cs` (7 tests, 39 assertions) | Atomic immutable snapshot, association-scoped reset, bounded stream cardinality and untrusted labels, UNKNOWN/PARTIAL/ANOMALY distinctions |
| [#487](https://github.com/masarray/arsas/pull/487#issuecomment-6418610935) P7.6D wire OptFlds provenance | `3f2bc75d9efb3bbe84c0808650b17f64bbda4808` | 5 | identical `ReportOptFldsProvenanceP76DTests.cs` (8 tests, 42 assertions) | Engine-decoded `OptionalFields` mapped into nullable OptFlds and bounded raw hex; missing/unknown/omitted/SqNum-advertised distinctions preserved without local MMS reparse |
| [#488](https://github.com/masarray/arsas/pull/488#issuecomment-6418614408) P7.6E GI/RCB activation causality | `d9d11ea2a24f09f6a515c3b561daca2a373d69c1` | 7 | identical `ReportActivationCausalityP76ETests.cs` (6 tests, 32 assertions) | Last attempted RptEna/GI write step, association-local start result, nullable GI/integrity decoded inclusion reason, no false clean reset/event-loss conclusions |
| [#476](https://github.com/masarray/arsas/pull/476#issuecomment-6418656598) P7.5 ServerAt / dual AP | `7384de375aad981b4dd75cf5d8c947d72dd4e656` | 13 | identical `SclServerAtEndpointSelectionTests.cs` | Exact SCL AP identity / declared endpoint / ServerAt model and lack of automatic MMS failover preserved; evolved #480 checks source SHA + IED + AP. Old #153 engine trial **not** repinned; current lock retains historical `sclServerAtTrial` physical qualification/no-auto-failover provenance. |

All six closed **unmerged**, deliberately. All changed paths present; common production files are usually **NOT** byte-identical because later engineering progressed. No intent to represent physical IEC 61850 data/event completeness from source ancestry alone.

**Test retention:** first five PRs preserve 40 distinct original regression methods, 193 original `Assert.` calls (37+43+39+42+32); #476 additional original SCL ServerAt test source preserved byte-for-byte. These are *inherited* regression suites, **not new tests created or newly run by D17**.

### Existing CI evidence, exact protected RC SHA
- [Windows Build 40165560413](https://github.com/masarray/arsas/actions/runs/40165560413) **SUCCESS**
- [SCL R7 40165560386](https://github.com/masarray/arsas/actions/runs/40165560386) **SUCCESS**
- [Interoperability Reference Guard 40165560409](https://github.com/masarray/arsas/actions/runs/40165560409) **SUCCESS**
- No CI new runs triggered by metadata-only closures; D16 describes other duplicate/failed/skipped checks on same SHA. These runs do not replace physical FAT.

## Why specific PRs/issues remain OPEN
- #606 GOOSE/SGCB and #615 Control safety pending owner and real IED acceptance, with #753–#756 stacked draft gates. Issue [#520](https://github.com/masarray/arsas/issues/520) is still real buffered RCB continuity/BufOvfl root-cause work; **no closure** from `#484/#485/#487/#488` retirement.
- #599 historical RCB default-collapse differs from RC; #594 current vs discovered RCB state not fully reconciled; #582 GOOSE SCL ownership and #595 COMTRADE row parity remain unique-delta audits.
- #660 direct preview EXE: original 3/4 changed paths byte-identical with RC but `.github/workflows/fat-direct-exe.yml` missing; **KEEP OPEN until equivalent current workflow proved**.
- #479 diagnostic field-intake scripts/docs missing in RC, current workflow evolved — **KEEP OPEN**.
- #454 original static acquisition path has missing historical helper and evolved semantic acquisition; requires deeper equivalence before closure.
- #486/#492 GUI dialogs and #569/#570/#577 operator UX / settings / angles require behavior-level confirmation (source changed). #490/#491 and #489 touch Control and currently safety-owned GUI/WPF paths; do not close or merge just on ancestry.
- #476 closure does **not** qualify physical Siemens J/F AP failover or engineer's real SCD; overarching [#446](https://github.com/masarray/arsas/issues/446) remains OPEN for interoperability/physical tests.
- #542 duplicate CI fan-out, #651 locked acceptance checkpoint, and 15 other independent issues are not presumed solved from PR closure.

## Live inventory (GitHub API paginated and verified)
D16 after actions **108 open PR / 16 independent open issues / 50 historical PR closures**. D17 after **six new verified closures**: **102 open PR / 16 independent issues / 56 historical PR closures**. ARIEC61850 independent backlog unchanged (3 open PR, 4 independent open issues at D16).

## Next handoff / D18
1. Prefer high-confidence complete source+test+original behavior checks, not blanket close by commit ancestry.
2. Audit #660 current direct EXE publish flow and #479 missing diagnostic scripts, deciding whether equivalent retained behavior exists; do not disable or replace FAT protection casually.
3. Resolve #594, #599, #595 and #582 with owner-visible operator semantics, distinguish status snapshots from actual live RCB state, and keep safety parity fail-closed.
4. Keep #753→#754→#755→#756 safety dependency correct, with physical R7/R10 acceptance gates; no auto-merge/release. Independent performance and GUI owners are not to be overwritten.
5. No need to rerun Windows CI merely to close metadata; only newly qualified source/test changes justify new runs.

**D17 engineering change:** strictly GitHub PR comments/status and this isolated audit documentation. No runtime source changes, no new build artifact, no physical IEC command/write tests claimed.
