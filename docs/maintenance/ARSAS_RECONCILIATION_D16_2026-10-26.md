# ARSAS D16 — Evidence-gated PR/Issue Cleanup (2026-10-26 WIB)

## Scope and exact provenance

This checkpoint continues D15 `8faa0f6a3663a5822b3479e2dbe0aa00dda2e204`. D16 is a **GitHub metadata reconciliation**, not source, engine, test, stable-release, or physical FAT qualification. Read D15 first.

Protected unchanged and re-verified at D16:
- ARSAS `main`: `14e5dddfbaae012b61a021366c1a864d83d316f2`.
- ARIEC61850 `main`: `3d21238d0b62f24faecd5948a4ee238c82b35966`.
- Published stable `v1.6.42` unchanged.
- Integrated RC `#727/#729`: `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, **both OPEN DRAFT; neither merged to `main`**.
- #753 `0652f66bed0c72dded20ff547e23e2ef1989341c` OPEN DRAFT.
- #754 `2aab054b6cf9eb5500b1dce5ef5957eb8ba0232d` OPEN DRAFT.
- #755 `7d235554225aa0341561b4e887a852fb6fd8e269` OPEN DRAFT, base RC.
- #756 `098ec4abdf7ce8681ec97111f12f3f6151eade78` OPEN DRAFT, base #755; retain staged dependency.
- ARIEC61850 trial engine pin `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` unchanged.

Inventory exact: **D15 initial 116 ARSAS open PR, 17 independent issues, 42 historical PR closed unmerged; D16 after actions: 111 open PR, 16 independent issues, 47 historical PR closed unmerged.** Engine retains three open PRs (#192, #196, #197), four independent issues.

## Actual GitHub actions completed in D16

**Five PRs closed unmerged, each with audit comment before closure:**

| PR | Disposition / verified rationale |
|---|---|
| [#480](https://github.com/masarray/arsas/pull/480#issuecomment-6418241033) | P7.6A AccessPoint provenance HEAD `0a5226b7f3bdc63653c823745fc3c242d14745ab` is RC ancestor (behind=0). All eight files retained; four exactly identical blobs (AP menu, endpoint topology, handoff, deterministic AP regression). Four evolved shared files individually checked for exact SHA/IED/AP binding, SclEndpointOrigin/choices and diagnostics; CI at exact RC head. No physical J↔F acceptance inferred. |
| [#481](https://github.com/masarray/arsas/pull/481#issuecomment-6418312184) | P7.6B evidence integrity HEAD `6f97f3b51895e724c9102822babc2e4ea7d821a5` is RC ancestor. Original test file byte-identical in RC; evolved tracker still rejects malformed IED, DataSet/FCDA and incomplete engine coverage. |
| [#482](https://github.com/masarray/arsas/pull/482#issuecomment-6418315098) | P7.6B bounded diagnostic semantic basis HEAD `0b84e81547ab8a5fded5c1f5f45e652c7dd3508b` is RC ancestor. Original test file byte-identical in RC; bounded escape/truncation/gating remains in current DiagnosticReportBuilder. |
| [#483](https://github.com/masarray/arsas/pull/483#issuecomment-6418318768) | P7.6B normalized DataSet semantic ordinal HEAD `5a05ab2187e04aaf70106ef5c59a0c625503aaef` is RC ancestor. Original test file byte-identical in RC; runtime retains SCL 1-based vs Discovery 0-based normalized semantic positions and independent duplicate/gap rejection. |
| [#546](https://github.com/masarray/arsas/pull/546#issuecomment-6418204316) | One-file **PROPOSED / DESIGN ONLY** historical IED Browser ARD. The document is absent from RC and explicitly **NOT** claimed merged. Closed as obsolete/superseded planning proposal rather than misrepresenting as implemented specification. Aspirational design requirements not assumed complete. |

**One completed milestone issue closed:**
- [#518](https://github.com/masarray/arsas/issues/518#issuecomment-6418251218) — specifically P10.6 BCUGE **simulator** static parity checkpoint already verified by independently supplied evidence. Closed `completed`. Open follow-up #520, #446 and ARIEC #162 continue independently; no real physical GE/R10 acceptance inferred.

These are metadata changes, **not new commits to release/main**. Closing ancestry-integrated PRs avoids duplicate cherry-picks and duplicate merge commits.

## Canonical evidence and semantic limits

- RC HEAD compare `HEAD(old PR)...817c3910` is a useful *necessary* ancestry check. `behind=0` **does not alone prove current behavior equivalence**; compare the actual changed path blob SHAs, test method/assertion semantics and newer implementation, then CI and owner acceptance.
- Verified successful at *exact* integrated RC SHA: [Windows Build 40165560413](https://github.com/masarray/arsas/actions/runs/40165560413), [SCL R7 40165560386](https://github.com/masarray/arsas/actions/runs/40165560386), [interoperability guard 40165560409](https://github.com/masarray/arsas/actions/runs/40165560409). Some independent earlier workflow duplicates for the same SHA were failed/skipped; do not generically assert all 30 CI runs success. No CI rerun requested by D16 metadata cleanup.
- #660: 3/4 historical files byte-identical in RC, but the actual `.github/workflows/fat-direct-exe.yml` is **missing**. **KEEP OPEN** pending evidence of equivalent delivery workflow, even though its original HEAD is an RC ancestor.
- #594: 0/2 historical blobs identical to RC. **KEEP OPEN** pending reconciliation of current vs discovery RCB state/provenance.
- #599: D15 proved Report collapse default difference. **KEEP OPEN** pending GUI owner UX approval; do not revert accepted RC.
- #582: original two regression tests byte-identical in RC but six implementation files evolved; GOOSE/SCL source authority still overlaps active #606/#753. **KEEP OPEN** pending original semantic parity review and device acceptance.
- #595: File Browser COMTRADE semantics require original per-record behavior verification, not only ancestry. **KEEP OPEN**.
- #569/#570/#577: original phasor/angle/SG semantics source/tests have materially evolved. **KEEP OPEN** pending exact source/semantic comparison.
- #454: 4/15 original paths identical to RC, one old source path absent; remaining evolved. Not closure-safe on simple ancestry.
- #492 and #557: none of original changed paths byte-identical to current RC (later GUI changes); require operator-visible equivalence audit.
- #479: workflow evolved and its three historical diagnostic-intake docs/scripts no longer exist in RC, so not closure-safe on ancestry alone.
- #606/#615 remain safety-owned, currently divergent from RC; retain #753–#756 draft control/GOOSE gates, physical FAT, UNKNOWN-command quarantine, fail-closed design.
- #716 Write Boolean, #726 accepted Write, #688 SG7 performance and #732 IP GUI are parallel owner lanes; no forced merge or closure.

## Next D17 tasks

1. Continue *small, high-confidence batch* audits of P7 stacked ancestors with exact original changed paths and regression assertions; make individual evidence comments and close only when zero unique semantic/ownership delta remains.
2. Resolve #660 delivery-workflow replacement and #594 RCB read-state differences at code/test level before any closure.
3. Reconcile #582 GOOSE SCL provenance against #753/#755/#756 only with owner approvals and physical device evidence.
4. Follow dependency: RC #727/#729 → #753/#754 safety → #755 combined → #756 epoch fence. Parent/child merge order must be proven rather than auto-merged. No CI-based production promotion.
5. Treat issue #651 as persistent accepted-behavior anti-regression checkpoint; do not close merely to reduce number.
6. Keep ARSAS `main`, engine lock, stable release, all concurrent owner branches and physical acceptance criteria unchanged.

No coding changes to runtime were necessary for these administrative closures. A separate safety engineering lane may continue P0 Command Inspection hazards from D15 without interference.
