# ARSAS — Open PR / Issue Reconciliation, 2026-10-26

**Cleanup decision log (live result after this initial snapshot):** [ARSAS_RECONCILIATION_DECISIONS_2026-10-26.md](ARSAS_RECONCILIATION_DECISIONS_2026-10-26.md) — initial 154 PR/19 issues → verified 139 PR/17 issues, 15 individual PR audit-trail closures and 2 verified code-only milestone closures. **The tables below are the immutable initial inventory, not the current open-only list.**


> **Read-only inventory checkpoint, not release acceptance or a declaration that any PR is redundant.** Snapshot verified by querying all pages of GitHub REST lists. This file lives on an audit-only branch until an appropriate docs-only promotion is approved by existing repository gates. Re-check live SHAs before every disposition.

## Immutable boundaries
- ARSAS consumer: https://github.com/masarray/arsas ; canonical main at audit start: `14e5dddfbaae012b61a021366c1a864d83d316f2`.
- ARIEC61850 upstream engine: https://github.com/masarray/ARIEC61850 ; upstream main observed `3d21238d0b62f24faecd5948a4ee238c82b35966`.
- Accepted public stable v1.6.42 source/tag `3db2ca98d144ab4b456e8dc1db9af18ad7a948f2`; release artifacts/checksums **untouched**.
- #729/#727 share head `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, with different base branches; **keep the separate qualification gates**.
- #726, #716, #688 and concurrent GUI/SSOT/SV branches protected pending individual proof.

## Inventory results
- 154 open PRs; 19 open issues (PRs excluded from issues REST response).
- 134 **branch-based** internal PR dependency edges, 110 distinct parent PRs, 44 terminal nodes. An edge means base branch name matches another open PR's head branch **only** — NOT proof of ancestry, equivalence, or preservation.
- Safety warning: #679 → #716 is **diverged** (verified GitHub commit comparison). Do not auto-close by edge alone.
- All entries below are Phase A inventory. Per-PR CI/reviews/unique file deltas are **not fully audited** yet. No status inference from age or old CI.

## Dependency graph (branch-linked, UNVERIFIED ancestry unless separately noted)
- [#679](https://github.com/masarray/arsas/pull/679) → [#716](https://github.com/masarray/arsas/pull/716)
- [#679](https://github.com/masarray/arsas/pull/679) → [#687](https://github.com/masarray/arsas/pull/687)
- [#672](https://github.com/masarray/arsas/pull/672) → [#674](https://github.com/masarray/arsas/pull/674)
- [#659](https://github.com/masarray/arsas/pull/659) → [#660](https://github.com/masarray/arsas/pull/660)
- [#658](https://github.com/masarray/arsas/pull/658) → [#659](https://github.com/masarray/arsas/pull/659)
- [#657](https://github.com/masarray/arsas/pull/657) → [#658](https://github.com/masarray/arsas/pull/658)
- [#656](https://github.com/masarray/arsas/pull/656) → [#657](https://github.com/masarray/arsas/pull/657)
- [#655](https://github.com/masarray/arsas/pull/655) → [#656](https://github.com/masarray/arsas/pull/656)
- [#648](https://github.com/masarray/arsas/pull/648) → [#649](https://github.com/masarray/arsas/pull/649)
- [#647](https://github.com/masarray/arsas/pull/647) → [#648](https://github.com/masarray/arsas/pull/648)
- [#646](https://github.com/masarray/arsas/pull/646) → [#647](https://github.com/masarray/arsas/pull/647)
- [#642](https://github.com/masarray/arsas/pull/642) → [#646](https://github.com/masarray/arsas/pull/646)
- [#644](https://github.com/masarray/arsas/pull/644) → [#645](https://github.com/masarray/arsas/pull/645)
- [#642](https://github.com/masarray/arsas/pull/642) → [#644](https://github.com/masarray/arsas/pull/644)
- [#634](https://github.com/masarray/arsas/pull/634) → [#643](https://github.com/masarray/arsas/pull/643)
- [#641](https://github.com/masarray/arsas/pull/641) → [#642](https://github.com/masarray/arsas/pull/642)
- [#640](https://github.com/masarray/arsas/pull/640) → [#641](https://github.com/masarray/arsas/pull/641)
- [#638](https://github.com/masarray/arsas/pull/638) → [#640](https://github.com/masarray/arsas/pull/640)
- [#634](https://github.com/masarray/arsas/pull/634) → [#639](https://github.com/masarray/arsas/pull/639)
- [#635](https://github.com/masarray/arsas/pull/635) → [#638](https://github.com/masarray/arsas/pull/638)
- [#634](https://github.com/masarray/arsas/pull/634) → [#637](https://github.com/masarray/arsas/pull/637)
- [#634](https://github.com/masarray/arsas/pull/634) → [#636](https://github.com/masarray/arsas/pull/636)
- [#634](https://github.com/masarray/arsas/pull/634) → [#635](https://github.com/masarray/arsas/pull/635)
- [#630](https://github.com/masarray/arsas/pull/630) → [#634](https://github.com/masarray/arsas/pull/634)
- [#632](https://github.com/masarray/arsas/pull/632) → [#633](https://github.com/masarray/arsas/pull/633)
- [#631](https://github.com/masarray/arsas/pull/631) → [#632](https://github.com/masarray/arsas/pull/632)
- [#630](https://github.com/masarray/arsas/pull/630) → [#631](https://github.com/masarray/arsas/pull/631)
- [#628](https://github.com/masarray/arsas/pull/628) → [#630](https://github.com/masarray/arsas/pull/630)
- [#627](https://github.com/masarray/arsas/pull/627) → [#628](https://github.com/masarray/arsas/pull/628)
- [#626](https://github.com/masarray/arsas/pull/626) → [#627](https://github.com/masarray/arsas/pull/627)
- [#625](https://github.com/masarray/arsas/pull/625) → [#626](https://github.com/masarray/arsas/pull/626)
- [#621](https://github.com/masarray/arsas/pull/621) → [#625](https://github.com/masarray/arsas/pull/625)
- [#621](https://github.com/masarray/arsas/pull/621) → [#624](https://github.com/masarray/arsas/pull/624)
- [#621](https://github.com/masarray/arsas/pull/621) → [#623](https://github.com/masarray/arsas/pull/623)
- [#621](https://github.com/masarray/arsas/pull/621) → [#622](https://github.com/masarray/arsas/pull/622)
- [#618](https://github.com/masarray/arsas/pull/618) → [#621](https://github.com/masarray/arsas/pull/621)
- [#618](https://github.com/masarray/arsas/pull/618) → [#620](https://github.com/masarray/arsas/pull/620)
- [#618](https://github.com/masarray/arsas/pull/618) → [#619](https://github.com/masarray/arsas/pull/619)
- [#617](https://github.com/masarray/arsas/pull/617) → [#618](https://github.com/masarray/arsas/pull/618)
- [#616](https://github.com/masarray/arsas/pull/616) → [#617](https://github.com/masarray/arsas/pull/617)
- [#614](https://github.com/masarray/arsas/pull/614) → [#616](https://github.com/masarray/arsas/pull/616)
- [#614](https://github.com/masarray/arsas/pull/614) → [#615](https://github.com/masarray/arsas/pull/615)
- [#613](https://github.com/masarray/arsas/pull/613) → [#614](https://github.com/masarray/arsas/pull/614)
- [#612](https://github.com/masarray/arsas/pull/612) → [#613](https://github.com/masarray/arsas/pull/613)
- [#611](https://github.com/masarray/arsas/pull/611) → [#612](https://github.com/masarray/arsas/pull/612)
- [#610](https://github.com/masarray/arsas/pull/610) → [#611](https://github.com/masarray/arsas/pull/611)
- [#609](https://github.com/masarray/arsas/pull/609) → [#610](https://github.com/masarray/arsas/pull/610)
- [#608](https://github.com/masarray/arsas/pull/608) → [#609](https://github.com/masarray/arsas/pull/609)
- [#607](https://github.com/masarray/arsas/pull/607) → [#608](https://github.com/masarray/arsas/pull/608)
- [#605](https://github.com/masarray/arsas/pull/605) → [#607](https://github.com/masarray/arsas/pull/607)
- [#605](https://github.com/masarray/arsas/pull/605) → [#606](https://github.com/masarray/arsas/pull/606)
- [#604](https://github.com/masarray/arsas/pull/604) → [#605](https://github.com/masarray/arsas/pull/605)
- [#603](https://github.com/masarray/arsas/pull/603) → [#604](https://github.com/masarray/arsas/pull/604)
- [#602](https://github.com/masarray/arsas/pull/602) → [#603](https://github.com/masarray/arsas/pull/603)
- [#601](https://github.com/masarray/arsas/pull/601) → [#602](https://github.com/masarray/arsas/pull/602)
- [#599](https://github.com/masarray/arsas/pull/599) → [#601](https://github.com/masarray/arsas/pull/601)
- [#598](https://github.com/masarray/arsas/pull/598) → [#599](https://github.com/masarray/arsas/pull/599)
- [#597](https://github.com/masarray/arsas/pull/597) → [#598](https://github.com/masarray/arsas/pull/598)
- [#596](https://github.com/masarray/arsas/pull/596) → [#597](https://github.com/masarray/arsas/pull/597)
- [#595](https://github.com/masarray/arsas/pull/595) → [#596](https://github.com/masarray/arsas/pull/596)
- [#594](https://github.com/masarray/arsas/pull/594) → [#595](https://github.com/masarray/arsas/pull/595)
- [#593](https://github.com/masarray/arsas/pull/593) → [#594](https://github.com/masarray/arsas/pull/594)
- [#590](https://github.com/masarray/arsas/pull/590) → [#591](https://github.com/masarray/arsas/pull/591)
- [#587](https://github.com/masarray/arsas/pull/587) → [#588](https://github.com/masarray/arsas/pull/588)
- [#586](https://github.com/masarray/arsas/pull/586) → [#587](https://github.com/masarray/arsas/pull/587)
- [#585](https://github.com/masarray/arsas/pull/585) → [#586](https://github.com/masarray/arsas/pull/586)
- [#584](https://github.com/masarray/arsas/pull/584) → [#585](https://github.com/masarray/arsas/pull/585)
- [#583](https://github.com/masarray/arsas/pull/583) → [#584](https://github.com/masarray/arsas/pull/584)
- [#582](https://github.com/masarray/arsas/pull/582) → [#583](https://github.com/masarray/arsas/pull/583)
- [#581](https://github.com/masarray/arsas/pull/581) → [#582](https://github.com/masarray/arsas/pull/582)
- [#580](https://github.com/masarray/arsas/pull/580) → [#581](https://github.com/masarray/arsas/pull/581)
- [#579](https://github.com/masarray/arsas/pull/579) → [#580](https://github.com/masarray/arsas/pull/580)
- [#577](https://github.com/masarray/arsas/pull/577) → [#579](https://github.com/masarray/arsas/pull/579)
- [#575](https://github.com/masarray/arsas/pull/575) → [#577](https://github.com/masarray/arsas/pull/577)
- [#574](https://github.com/masarray/arsas/pull/574) → [#576](https://github.com/masarray/arsas/pull/576)
- [#574](https://github.com/masarray/arsas/pull/574) → [#575](https://github.com/masarray/arsas/pull/575)
- [#573](https://github.com/masarray/arsas/pull/573) → [#574](https://github.com/masarray/arsas/pull/574)
- [#571](https://github.com/masarray/arsas/pull/571) → [#573](https://github.com/masarray/arsas/pull/573)
- [#571](https://github.com/masarray/arsas/pull/571) → [#572](https://github.com/masarray/arsas/pull/572)
- [#570](https://github.com/masarray/arsas/pull/570) → [#571](https://github.com/masarray/arsas/pull/571)
- [#569](https://github.com/masarray/arsas/pull/569) → [#570](https://github.com/masarray/arsas/pull/570)
- [#568](https://github.com/masarray/arsas/pull/568) → [#569](https://github.com/masarray/arsas/pull/569)
- [#567](https://github.com/masarray/arsas/pull/567) → [#568](https://github.com/masarray/arsas/pull/568)
- [#563](https://github.com/masarray/arsas/pull/563) → [#567](https://github.com/masarray/arsas/pull/567)
- [#560](https://github.com/masarray/arsas/pull/560) → [#566](https://github.com/masarray/arsas/pull/566)
- [#560](https://github.com/masarray/arsas/pull/560) → [#563](https://github.com/masarray/arsas/pull/563)
- [#560](https://github.com/masarray/arsas/pull/560) → [#561](https://github.com/masarray/arsas/pull/561)
- [#559](https://github.com/masarray/arsas/pull/559) → [#560](https://github.com/masarray/arsas/pull/560)
- [#558](https://github.com/masarray/arsas/pull/558) → [#559](https://github.com/masarray/arsas/pull/559)
- [#557](https://github.com/masarray/arsas/pull/557) → [#558](https://github.com/masarray/arsas/pull/558)
- [#556](https://github.com/masarray/arsas/pull/556) → [#557](https://github.com/masarray/arsas/pull/557)
- [#555](https://github.com/masarray/arsas/pull/555) → [#556](https://github.com/masarray/arsas/pull/556)
- [#554](https://github.com/masarray/arsas/pull/554) → [#555](https://github.com/masarray/arsas/pull/555)
- [#553](https://github.com/masarray/arsas/pull/553) → [#554](https://github.com/masarray/arsas/pull/554)
- [#552](https://github.com/masarray/arsas/pull/552) → [#553](https://github.com/masarray/arsas/pull/553)
- [#547](https://github.com/masarray/arsas/pull/547) → [#552](https://github.com/masarray/arsas/pull/552)
- [#512](https://github.com/masarray/arsas/pull/512) → [#543](https://github.com/masarray/arsas/pull/543)
- [#511](https://github.com/masarray/arsas/pull/511) → [#512](https://github.com/masarray/arsas/pull/512)
- [#510](https://github.com/masarray/arsas/pull/510) → [#511](https://github.com/masarray/arsas/pull/511)
- [#509](https://github.com/masarray/arsas/pull/509) → [#510](https://github.com/masarray/arsas/pull/510)
- [#508](https://github.com/masarray/arsas/pull/508) → [#509](https://github.com/masarray/arsas/pull/509)
- [#507](https://github.com/masarray/arsas/pull/507) → [#508](https://github.com/masarray/arsas/pull/508)
- [#506](https://github.com/masarray/arsas/pull/506) → [#507](https://github.com/masarray/arsas/pull/507)
- [#505](https://github.com/masarray/arsas/pull/505) → [#506](https://github.com/masarray/arsas/pull/506)
- [#504](https://github.com/masarray/arsas/pull/504) → [#505](https://github.com/masarray/arsas/pull/505)
- [#501](https://github.com/masarray/arsas/pull/501) → [#504](https://github.com/masarray/arsas/pull/504)
- [#501](https://github.com/masarray/arsas/pull/501) → [#503](https://github.com/masarray/arsas/pull/503)
- [#501](https://github.com/masarray/arsas/pull/501) → [#502](https://github.com/masarray/arsas/pull/502)
- [#500](https://github.com/masarray/arsas/pull/500) → [#501](https://github.com/masarray/arsas/pull/501)
- [#499](https://github.com/masarray/arsas/pull/499) → [#500](https://github.com/masarray/arsas/pull/500)
- [#498](https://github.com/masarray/arsas/pull/498) → [#499](https://github.com/masarray/arsas/pull/499)
- [#497](https://github.com/masarray/arsas/pull/497) → [#498](https://github.com/masarray/arsas/pull/498)
- [#494](https://github.com/masarray/arsas/pull/494) → [#497](https://github.com/masarray/arsas/pull/497)
- [#494](https://github.com/masarray/arsas/pull/494) → [#496](https://github.com/masarray/arsas/pull/496)
- [#492](https://github.com/masarray/arsas/pull/492) → [#494](https://github.com/masarray/arsas/pull/494)
- [#491](https://github.com/masarray/arsas/pull/491) → [#492](https://github.com/masarray/arsas/pull/492)
- [#489](https://github.com/masarray/arsas/pull/489) → [#491](https://github.com/masarray/arsas/pull/491)
- [#488](https://github.com/masarray/arsas/pull/488) → [#490](https://github.com/masarray/arsas/pull/490)
- [#488](https://github.com/masarray/arsas/pull/488) → [#489](https://github.com/masarray/arsas/pull/489)
- [#487](https://github.com/masarray/arsas/pull/487) → [#488](https://github.com/masarray/arsas/pull/488)
- [#485](https://github.com/masarray/arsas/pull/485) → [#487](https://github.com/masarray/arsas/pull/487)
- [#485](https://github.com/masarray/arsas/pull/485) → [#486](https://github.com/masarray/arsas/pull/486)
- [#484](https://github.com/masarray/arsas/pull/484) → [#485](https://github.com/masarray/arsas/pull/485)
- [#483](https://github.com/masarray/arsas/pull/483) → [#484](https://github.com/masarray/arsas/pull/484)
- [#482](https://github.com/masarray/arsas/pull/482) → [#483](https://github.com/masarray/arsas/pull/483)
- [#481](https://github.com/masarray/arsas/pull/481) → [#482](https://github.com/masarray/arsas/pull/482)
- [#480](https://github.com/masarray/arsas/pull/480) → [#481](https://github.com/masarray/arsas/pull/481)
- [#476](https://github.com/masarray/arsas/pull/476) → [#480](https://github.com/masarray/arsas/pull/480)
- [#476](https://github.com/masarray/arsas/pull/476) → [#479](https://github.com/masarray/arsas/pull/479)
- [#473](https://github.com/masarray/arsas/pull/473) → [#476](https://github.com/masarray/arsas/pull/476)
- [#471](https://github.com/masarray/arsas/pull/471) → [#473](https://github.com/masarray/arsas/pull/473)
- [#466](https://github.com/masarray/arsas/pull/466) → [#471](https://github.com/masarray/arsas/pull/471)
- [#459](https://github.com/masarray/arsas/pull/459) → [#466](https://github.com/masarray/arsas/pull/466)
- [#454](https://github.com/masarray/arsas/pull/454) → [#459](https://github.com/masarray/arsas/pull/459)

## All open PRs
| PR | Title | Head SHA prefix | Base branch | State | Dependent open PR(s) | Audit disposition |
|---|---|---|---|---|---|---|
| [#732](https://github.com/masarray/arsas/pull/732) | P1 UI: unify premium cool-blue inputs, dropdowns, dialogs and empty states (#730) | `c47a62203711` | `ui/p0-smv-coolblue-normal-window-20261024` | Draft | — | Pending per-PR semantic/CI qualification |
| [#729](https://github.com/masarray/arsas/pull/729) | P0: reconcile v1.6.42 shipped source + stable docs to main (physical R7/R10 gates pending) | `817c3910e822` | `main` | Draft | — | Pending per-PR semantic/CI qualification |
| [#727](https://github.com/masarray/arsas/pull/727) | RC0 INTEGRATED — GUI P0/P1/P2 + SSOT/Write + main SV; physical release gates open | `817c3910e822` | `integration/arsas-ssot-performance-p0-20261024` | Draft | — | Pending per-PR semantic/CI qualification |
| [#726](https://github.com/masarray/arsas/pull/726) | INTEGRATION GATE ONLY: user-accepted Write onto latest SSOT V2; preserve advanced SMV | `42c7cfe1364f` | `fix/p0-static-report-ui-responsive-20261024` | Draft | — | Pending per-PR semantic/CI qualification |
| [#716](https://github.com/masarray/arsas/pull/716) | FAT Write safety: reject unrecognized live Boolean instead of selecting false | `f53857d5f66d` | `fix/fat-integrated-quality-attention-20261022` | Draft | — | Pending per-PR semantic/CI qualification |
| [#688](https://github.com/masarray/arsas/pull/688) | P0 SG7_BCU: async Browser model indexing, bounded metrics and CF-aware units | `774321313aac` | `fix/sg7-dock-scroll-browser-write-20261023` | Draft | — | Pending per-PR semantic/CI qualification |
| [#687](https://github.com/masarray/arsas/pull/687) | FAST FAT EXE: compact Smart IP UI + SG7 staging engine (no ZIP) | `c5166fdbffd1` | `fix/fat-integrated-quality-attention-20261022` | Draft | — | Pending per-PR semantic/CI qualification |
| [#679](https://github.com/masarray/arsas/pull/679) | FAT P0 integration checkpoint: invalid quality vector in Browser and Device Explorer | `04350e15f9be` | `feat/smv-sharp-latest-p0-20261022` | Draft | #716, #687 | Pending per-PR semantic/CI qualification |
| [#677](https://github.com/masarray/arsas/pull/677) | Fast workflow: download PR portable .exe directly from Actions, no ZIP | `46b90e9bc51d` | `main` | Draft | — | Pending per-PR semantic/CI qualification |
| [#674](https://github.com/masarray/arsas/pull/674) | P0 GR_X SCD: exact CSWI CO/live MMS parity, explicit selector and bounded diagnostics | `391c65687273` | `fix/p0-cswi-cross-dataset-discovery-20261022` | Draft | — | Pending per-PR semantic/CI qualification |
| [#673](https://github.com/masarray/arsas/pull/673) | P0 GR_X: make CSWI control candidates discoverable independently of XCBR/XSWI status | `539805c97072` | `fix/p0-m1-typed-mms-strings-20261022` | Draft | — | Pending per-PR semantic/CI qualification |
| [#672](https://github.com/masarray/arsas/pull/672) | P0 GR_X: live-verify cross-DataSet CSWI.Pos in Command Dock (no unsafe status-only bypass) | `1b9ce4f3193e` | `fix/p0-m1-typed-mms-strings-20261022` | Draft | #674 | Pending per-PR semantic/CI qualification |
| [#666](https://github.com/masarray/arsas/pull/666) | BLOCKED P0 SBO timing FAT trial: exact engine #187 pin requires provenance-guard qualification | `7626556c29e2` | `fix/p0-command-stoprace-admission-telemetry-20261021` | Draft | — | Pending per-PR semantic/CI qualification |
| [#663](https://github.com/masarray/arsas/pull/663) | P0 SIEBCU: command must not queue, phase-smart Device Explorer labels, engine discovery follow-up | `03c668cb2474` | `fix/p0-siebcu-bounded-read-ui-stability-20261021` | Draft | — | Pending per-PR semantic/CI qualification |
| [#660](https://github.com/masarray/arsas/pull/660) | CI/FAT: direct EXE preview asset distribution with explicit manual public prerelease gate | `6cd9a1d04690` | `fix/p0-model-parent-sg-scalar-ssot-20261021` | Draft | — | Pending per-PR semantic/CI qualification |
| [#659](https://github.com/masarray/arsas/pull/659) | P0 Data Model: cross-IED typed SG parent scalar reuse + bounded non-scalar child evidence | `1371c2990c40` | `fix/p0-report-companion-ledger-ssot-20261021` | Draft | #660 | Pending per-PR semantic/CI qualification |
| [#658](https://github.com/masarray/arsas/pull/658) | P0: canonical Report process q/t SSOT merge (fix Unknown reversion in Live Signal projection) | `c62a9c8a3186` | `fix/p0-q-schema-ssot-reconcile-20261021` | Draft | #659 | Pending per-PR semantic/CI qualification |
| [#657](https://github.com/masarray/arsas/pull/657) | P0 quality follow-up: reconcile stale inventory q hints using exact model SSOT | `81cde9e0bd2f` | `fix/p0-typed-substitution-report-quality-20261021` | Draft | #658 | Pending per-PR semantic/CI qualification |
| [#656](https://github.com/masarray/arsas/pull/656) | P0: typed SV substitution and exact Report q→signal Quality projection | `1314005f7f44` | `trial/p0-i-exact-engine-stage-evidence-20261021` | Draft | #657 | Pending per-PR semantic/CI qualification |
| [#655](https://github.com/masarray/arsas/pull/655) | P0-I: exact-SHA consumer/engine trial for stage-attributed SBOw without changing control safety | `069a32adfae6` | `trial/p0-read-command-integrated-20261021` | Draft | #656 | Pending per-PR semantic/CI qualification |
| [#653](https://github.com/masarray/arsas/pull/653) | CI P4: fail-closed docs-only fast gate and opt-in Field Capture Windows packaging | `779bbdef9865` | `ui/command-ctlmodel-static-glyph-20261021` | Draft | — | Pending per-PR semantic/CI qualification |
| [#649](https://github.com/masarray/arsas/pull/649) | P0 Field: restore ZIV StatusOnly controls in dock; enable IED GOOSE only with GoCB evidence | `4c232184f645` | `ui/ied-card-integrated-selection-phase3-20261020` | Draft | — | Pending per-PR semantic/CI qualification |
| [#648](https://github.com/masarray/arsas/pull/648) | P0 UI Phase 3: integrate selected IED blue accent into rounded card edge | `109b1f93eea0` | `ui/command-dock-focus-discovery-line-20261020` | Draft | #649 | Pending per-PR semantic/CI qualification |
| [#647](https://github.com/masarray/arsas/pull/647) | P0 UI-2: remove persistent Command Dock focus border; unify discovery + Browser Read under-IP progress | `f0831fa0a92f` | `ui/command-dock-white-icons-20261020` | Draft | #648 | Pending per-PR semantic/CI qualification |
| [#646](https://github.com/masarray/arsas/pull/646) | P0 UI-1: white transparent Command Dock chevron and power SVG, freeze FAT checkpoint | `d14a6e309326` | `trial/arsas-command-leaf-fabric-20261020` | Draft | #647 | Pending per-PR semantic/CI qualification |
| [#645](https://github.com/masarray/arsas/pull/645) | P0-H: fail-closed canonical Report alias collisions and sibling-phase fallback | `2b4e4ea0b917` | `fix/p0-f-browser-catalog-authority-20261020` | Draft | — | Pending per-PR semantic/CI qualification |
| [#644](https://github.com/masarray/arsas/pull/644) | P0-F: fail-closed Browser Read admission against active MMS model | `80e4836affce` | `trial/arsas-command-leaf-fabric-20261020` | Draft | #645 | Pending per-PR semantic/CI qualification |
| [#643](https://github.com/masarray/arsas/pull/643) | P0-F: read model-verified SV/OR/BL attributes and fixed inline IEDCard progress | `d6bbb357873a` | `fix/p0-e-smart-projection-report-causality-20261020` | Draft | — | Pending per-PR semantic/CI qualification |
| [#642](https://github.com/masarray/arsas/pull/642) | P0-FAT: combined Command Dock + Browser leaf + IED-card progress trial | `30a7f18e8e57` | `fix/command-dock-event-driven-20261020` | Draft | #646, #644 | Pending per-PR semantic/CI qualification |
| [#641](https://github.com/masarray/arsas/pull/641) | P0-V: event-driven Command Dock and atomic typed-control inspection | `17b339c0877e` | `fix/command-dock-stable-projection-20261020` | Draft | #642 | Pending per-PR semantic/CI qualification |
| [#640](https://github.com/masarray/arsas/pull/640) | P0-U: stable Command Dock row lifetime and architectural latency plan | `d7b16f21e091` | `fix/control-priority-inspection-20261020` | Draft | #641 | Pending per-PR semantic/CI qualification |
| [#639](https://github.com/masarray/arsas/pull/639) | P0-F: stable thin Browser Read progress on IED card and truthful SV/OR/BL unattempted status | `00a88e7bf942` | `fix/p0-e-smart-projection-report-causality-20261020` | Draft | — | Pending per-PR semantic/CI qualification |
| [#638](https://github.com/masarray/arsas/pull/638) | P0-T: prioritize operator commands over Command Dock inspection backlog | `0e00d530dafe` | `fix/control-latency-field-20261020` | Draft | #640 | Pending per-PR semantic/CI qualification |
| [#637](https://github.com/masarray/arsas/pull/637) | P0-F: explicit verified SV/OR/BL Browser Read and fixed-height IED card progress underline | `4ee4ec8965e2` | `fix/p0-e-smart-projection-report-causality-20261020` | Draft | — | Pending per-PR semantic/CI qualification |
| [#636](https://github.com/masarray/arsas/pull/636) | P0-G: model-proven Browser SV/OR/BL reads and fixed IED-card micro-progress | `3367f3345098` | `fix/p0-e-smart-projection-report-causality-20261020` | Draft | — | Pending per-PR semantic/CI qualification |
| [#635](https://github.com/masarray/arsas/pull/635) | Control responsiveness trial: stop redundant dock inspection and isolate RCB diagnostics | `2aa0461d672d` | `fix/p0-e-smart-projection-report-causality-20261020` | Draft | #638 | Pending per-PR semantic/CI qualification |
| [#634](https://github.com/masarray/arsas/pull/634) | P0-E: index Smart Discovery fallback references and preserve BRCB event cause truth | `0a7e555df10a` | `fix/p0-d-live-signal-viewport-stacked-20261020` | Draft | #643, #639, #637, #636, #635 | Pending per-PR semantic/CI qualification |
| [#633](https://github.com/masarray/arsas/pull/633) | P0-H: trial-pin CI-tested Edition 2.2 engine with immutable P0-C ancestry | `e3962b6fefbd` | `fix/p0-f-report-rcb-stream-identity-20261020` | Draft | — | Pending per-PR semantic/CI qualification |
| [#632](https://github.com/masarray/arsas/pull/632) | P0-F: fail-closed RCB continuity identity across Report metadata aliases | `1d2ccfd6d392` | `fix/p0-e-sgcb-read-coverage-evidence-20261020` | Draft | #633 | Pending per-PR semantic/CI qualification |
| [#631](https://github.com/masarray/arsas/pull/631) | P0-E: exact SGCB Read completion accounting without fabricated MMS attempts | `b3527d18735f` | `fix/p0-d-live-signal-viewport-stacked-20261020` | Draft | #632 | Pending per-PR semantic/CI qualification |
| [#630](https://github.com/masarray/arsas/pull/630) | P0-D stacked: restore bounded Live Signal viewport without duplicate MMS logic | `46e6df528db3` | `fix/p0-d-field-evidence-ui-release-20261019` | Draft | #634, #631 | Pending per-PR semantic/CI qualification |
| [#628](https://github.com/masarray/arsas/pull/628) | P0-D: field-proven zero-length OCTET, exact LN Read coverage and report ingress UX | `ca54fa2c6958` | `fix/p0-c-typed-read-ssot-consumer-20261019` | Draft | #630 | Pending per-PR semantic/CI qualification |
| [#627](https://github.com/masarray/arsas/pull/627) | P0-C: typed SG read outcome evidence, exact FC/SSOT parent projection | `69e6a6de171b` | `fix/p0-b-target-scoped-rcb-consumer-20261019` | Draft | #628 | Pending per-PR semantic/CI qualification |
| [#626](https://github.com/masarray/arsas/pull/626) | P0-B: pin bounded DataSet-scoped RCB pager and preserve safety policy | `92a6b7fa00c5` | `fix/p0-a-verified-identity-engine-integration-20261019` | Draft | #627 | Pending per-PR semantic/CI qualification |
| [#625](https://github.com/masarray/arsas/pull/625) | P0-A: integrate verified identity engine trial with endpoint/AP-bound SCL | `5c9f2732f9ce` | `fix/m217m-ssot-datamodel-focused-admission-20261017` | Draft | #626 | Pending per-PR semantic/CI qualification |
| [#624](https://github.com/masarray/arsas/pull/624) | P0-A: SCL verified live IED identity integrated with exact engine pin | `77d72550c876` | `fix/m217m-ssot-datamodel-focused-admission-20261017` | Draft | — | Pending per-PR semantic/CI qualification |
| [#623](https://github.com/masarray/arsas/pull/623) | P0-A: five-LD identity authority, SHA-pinned SCL AP verification and engine trial | `c6b0090e75b7` | `fix/m217m-ssot-datamodel-focused-admission-20261017` | Draft | — | Pending per-PR semantic/CI qualification |
| [#622](https://github.com/masarray/arsas/pull/622) | P0 global interoperability: verified identity and target-scoped RCB engine trial | `2eb85e0c74fc` | `fix/m217m-ssot-datamodel-focused-admission-20261017` | Draft | — | Pending per-PR semantic/CI qualification |
| [#621](https://github.com/masarray/arsas/pull/621) | M2.17-M P0: reconcile #618/#620; canonical Data Model focused hydration + Explorer read SSOT | `3276b0e5068c` | `fix/m217k-consumer-typed-st-report-engine-trial-20261017` | Draft | #625, #624, #623, #622 | Pending per-PR semantic/CI qualification |
| [#620](https://github.com/masarray/arsas/pull/620) | M2.17-L: bounded on-selection SGCB / Data Model value hydration | `53c3d7ecce49` | `fix/m217k-consumer-typed-st-report-engine-trial-20261017` | Draft | — | Pending per-PR semantic/CI qualification |
| [#619](https://github.com/masarray/arsas/pull/619) | M2.17-L: bounded read-on-open IED Browser and exact MX/CF unit context | `f04efac32ff3` | `fix/m217k-consumer-typed-st-report-engine-trial-20261017` | Draft | — | Pending per-PR semantic/CI qualification |
| [#618](https://github.com/masarray/arsas/pull/618) | M2.17-K: isolated ARIEC PR173 typed C264 report integration trial | `d3a7021376b3` | `fix/m217j-operator-reliability-rcb-control-grid-20261017` | Draft | #621, #620, #619 | Pending per-PR semantic/CI qualification |
| [#617](https://github.com/masarray/arsas/pull/617) | M2.17-J: physical C264 UI/data-truth regressions — operable Command Dock, RCB authority, SGCB clarity | `527b834127d2` | `fix/m217i-dataset-exact-fcda-and-observation-coverage-20261017` | Draft | #618 | Pending per-PR semantic/CI qualification |
| [#616](https://github.com/masarray/arsas/pull/616) | M2.17-I: exact FCDA SCL projection and bounded phasor q/t+angle acquisition | `da9ab79e1d54` | `fix/p0-command-ied-singleflight-timeout-quarantine-20261017` | Draft | #617 | Pending per-PR semantic/CI qualification |
| [#615](https://github.com/masarray/arsas/pull/615) | P0 follow-up: retire unresolved MMS control sessions without blocking reconnect | `b22f0f8e92e7` | `fix/p0-command-ied-singleflight-timeout-quarantine-20261017` | Draft | — | Pending per-PR semantic/CI qualification |
| [#614](https://github.com/masarray/arsas/pull/614) | P0: prevent consecutive BCUGE command stalls with single-flight and uncertain-timeout quarantine | `35caaddd7a60` | `feat/m217h-dataset-verified-mx-initial-seed-20261016` | Draft | #616, #615 | Pending per-PR semantic/CI qualification |
| [#613](https://github.com/masarray/arsas/pull/613) | M2.17-H: bounded initial MMXU MX hydration from verified DataSet membership | `4984174c9fae` | `fix/m217g-dataset-mx-structure-and-typed-evidence-20261016` | Draft | #614 | Pending per-PR semantic/CI qualification |
| [#612](https://github.com/masarray/arsas/pull/612) | M2.17-G: exact DataSet MX schema fallback and truthful q/t evidence status | `f42f35695c36` | `fix/m217f-inline-goose-stream-browser-20261016` | Draft | #613 | Pending per-PR semantic/CI qualification |
| [#611](https://github.com/masarray/arsas/pull/611) | M2.17-F: in-place GOOSE Subscribe and verified/unbound allData display | `80bc2dab2d80` | `fix/m217e-goose-ttl-browser-acceptance-20261016` | Draft | #612 | Pending per-PR semantic/CI qualification |
| [#610](https://github.com/masarray/arsas/pull/610) | M2.17-E: GOOSE Browser TTL correctness and passive acceptance diagnostics | `e110c63f962b` | `fix/m217d-rcb-session-and-dataset-fc-parity-20261016` | Draft | #611 | Pending per-PR semantic/CI qualification |
| [#609](https://github.com/masarray/arsas/pull/609) | M2.17-D: association-safe RCB status and strict MX FCD/FCDA Read/Report parity | `1ea22d498b37` | `fix/m217d-initial-read-failure-provenance-20261016` | Draft | #610 | Pending per-PR semantic/CI qualification |
| [#608](https://github.com/masarray/arsas/pull/608) | M2.17-D: surface SGCB/GoCB initial MMS errors in Browser and Diagnostics | `0034f72e636a` | `fix/m217d-goose-entry-rcb-sections-files-inset-20261016` | Draft | #609 | Pending per-PR semantic/CI qualification |
| [#607](https://github.com/masarray/arsas/pull/607) | M2.17-D: verified SGCB/GoCB status hydration, GOOSE subscribe and scoped Data Model Read | `c5171a76906a` | `feat/m217c-bounded-priority-live-hydration-20261016` | Draft | #608 | Pending per-PR semantic/CI qualification |
| [#606](https://github.com/masarray/arsas/pull/606) | M2.17-D Field convergence: GoCB passive evidence, SGCB initial status, RCB/Read correctness | `9474a5ca07e1` | `feat/m217c-bounded-priority-live-hydration-20261016` | Draft | — | Pending per-PR semantic/CI qualification |
| [#605](https://github.com/masarray/arsas/pull/605) | M2.17-C Data Model: bounded priority MMS hydration on full live discovery | `eb44abc9b2ab` | `fix/m217b-reuse-verified-scl-initial-evidence-20261016` | Draft | #607, #606 | Pending per-PR semantic/CI qualification |
| [#604](https://github.com/masarray/arsas/pull/604) | M2.17-B Live Model: reuse verified SCL initial MMS values in per-IED SSOT | `5a83f4510bf7` | `fix/m217-typed-dpc-live-model-presentation-20261016` | Draft | #605 | Pending per-PR semantic/CI qualification |
| [#603](https://github.com/masarray/arsas/pull/603) | M2.17 Browser SSOT: IEC DPC Open [01] / Close [10] from exact typed Pos evidence | `b59f4951065d` | `fix/m216-browser-time-quality-files-20261016` | Draft | #604 | Pending per-PR semantic/CI qualification |
| [#602](https://github.com/masarray/arsas/pull/602) | M2.16 BCUGE Browser: operator-local UTC, nested MX raw q/t and cool-blue Files grid | `0069b542ac12` | `fix/m214-rio-root-compat-without-engine-repin-20261016` | Draft | #603 | Pending per-PR semantic/CI qualification |
| [#601](https://github.com/masarray/arsas/pull/601) | M2.14→M2.15 BCUGE: complete CFG/DAT/RIO bundles on verified MMS root without engine repin | `1a8601071fc9` | `feat/m214-browser-collapsible-report-sections-20261016` | Draft | #602 | Pending per-PR semantic/CI qualification |
| [#599](https://github.com/masarray/arsas/pull/599) | M2.14 IED Browser: collapsible RCB control and last-report sections | `70ad34392fd9` | `fix/m214-browser-per-attribute-read-failures-20261016` | Draft | #601 | Pending per-PR semantic/CI qualification |
| [#598](https://github.com/masarray/arsas/pull/598) | M2.14 BCUGE: expose exact failed SG/DA MMS reads instead of ambiguous Not read | `3b87e653cd73` | `fix/m214-browser-isolated-schema-fidelity-20261016` | Draft | #599 | Pending per-PR semantic/CI qualification |
| [#597](https://github.com/masarray/arsas/pull/597) | M2.14 BCUGE: restore ARIEC schema binding on isolated Browser MMS reads | `326525c90cec` | `fix/m214-browser-goose-scl-network-fidelity-20261016` | Draft | #598 | Pending per-PR semantic/CI qualification |
| [#596](https://github.com/masarray/arsas/pull/596) | M2.14 GOOSE: preserve verified SCL VLAN and timing in Browser details | `5bf72735c858` | `fix/m214-browser-files-bundle-grid-20261016` | Draft | #597 | Pending per-PR semantic/CI qualification |
| [#595](https://github.com/masarray/arsas/pull/595) | M2.14 Browser Files: one verified COMTRADE record per row, compact native grid | `8d29512f8a3a` | `fix/m214-rcb-discovery-state-provenance-20261016` | Draft | #596 | Pending per-PR semantic/CI qualification |
| [#594](https://github.com/masarray/arsas/pull/594) | M2.14 Browser: surface RCB discovery-state fields with exact provenance | `0386bd10e53f` | `fix/m214-browser-files-root-first-20261015` | Draft | #595 | Pending per-PR semantic/CI qualification |
| [#593](https://github.com/masarray/arsas/pull/593) | M2.14 Files: show root MMS inventory first; bound COMTRADE enrichment | `9fbb92eb9184` | `fix/m214-smv-tab-passive-entry-20261015` | Draft | #594 | Pending per-PR semantic/CI qualification |
| [#591](https://github.com/masarray/arsas/pull/591) | M2.14 IED Browser: fail closed on unknown/structured MMS Read results | `c4a959b0fcd3` | `fix/m214-iedbrowser-co-read-status-20261015` | Draft | — | Pending per-PR semantic/CI qualification |
| [#590](https://github.com/masarray/arsas/pull/590) | M2.14 IED Browser: distinguish FC CO controls from missing reads | `6b2c42ee7d33` | `feat/m214-operator-nav-smv-20261015` | Draft | #591 | Pending per-PR semantic/CI qualification |
| [#588](https://github.com/masarray/arsas/pull/588) | M2.14 Browser: Browse Files button opens actual Files workspace | `2001a359a1cb` | `fix/m214-sgcb-last-changed-zero-time-20261015` | Draft | — | Pending per-PR semantic/CI qualification |
| [#587](https://github.com/masarray/arsas/pull/587) | M2.14 SGCB: preserve LActTm wire quality and distinguish unset time | `5ef905ecefbe` | `fix/m214-filedirectory-inventory-20261015` | Draft | #588 | Pending per-PR semantic/CI qualification |
| [#586](https://github.com/masarray/arsas/pull/586) | M2.14 Browser: show actual MMS FileDirectory entries beyond fault records | `3bdeff82affc` | `fix/m214-report-quality-dataref-fence-20261015` | Draft | #587 | Pending per-PR semantic/CI qualification |
| [#585](https://github.com/masarray/arsas/pull/585) | M2.14 Browser: fence raw Report quality by exact DataRef and FC | `02ed1e3b33ab` | `feat/m214-browser-row-click-do-read-20261015` | Draft | #586 | Pending per-PR semantic/CI qualification |
| [#584](https://github.com/masarray/arsas/pull/584) | M2.14 Browser: single-click row expansion and full selected-DO Read | `5691e3fed31c` | `fix/m214-operator-contours-20261015` | Draft | #585 | Pending per-PR semantic/CI qualification |
| [#583](https://github.com/masarray/arsas/pull/583) | M2.14 GUI: exact operator Explorer, Diagnostics and Command Dock icons | `756de98a2bb0` | `fix/m214-bound-goose-and-tapchg-evidence-20261015` | Draft | #584 | Pending per-PR semantic/CI qualification |
| [#582](https://github.com/masarray/arsas/pull/582) | M2.14: recover bound SCL GOOSE and report-observed TapChg parent | `38ad98f74c97` | `fix/m214-field-acceptance-ci-angle-20261015` | Draft | #583 | Pending per-PR semantic/CI qualification |
| [#581](https://github.com/masarray/arsas/pull/581) | M2.14 field acceptance: correct normative vector-angle test contract | `a9e6c1c4a4db` | `feat/m214-field-ux-icons-sg-20261015` | Draft | #582 | Pending per-PR semantic/CI qualification |
| [#580](https://github.com/masarray/arsas/pull/580) | M2.14 field GUI: operator icons, wide nav, IEC angle, SG SCL fallback | `96f1c80301af` | `feat/m214-dataset-multileaf-evidence-20261015` | Draft | #581 | Pending per-PR semantic/CI qualification |
| [#579](https://github.com/masarray/arsas/pull/579) | M2.14 DataSet: show observed multi-leaf ST values without guessed parent | `07b7b1820aeb` | `feat/m214-browser-engineering-hierarchy-20261015` | Draft | #580 | Pending per-PR semantic/CI qualification |
| [#577](https://github.com/masarray/arsas/pull/577) | M2.14 GUI: one IEC DO hierarchy with compact engineering values | `4be7b51b9040` | `ui/material-icons-consistency-20261015` | Draft | #579 | Pending per-PR semantic/CI qualification |
| [#576](https://github.com/masarray/arsas/pull/576) | M2.14 UI: unify navigation and first-run Add IED on Material icon system | `5cfb3cd440d3` | `fix/m214-operator-icon-single-source-20261015` | Draft | — | Pending per-PR semantic/CI qualification |
| [#575](https://github.com/masarray/arsas/pull/575) | UI: unify Material icons across ARSAS navigation, onboarding and primary toolbars | `7a761feecbda` | `fix/m214-operator-icon-single-source-20261015` | Draft | #577 | Pending per-PR semantic/CI qualification |
| [#574](https://github.com/masarray/arsas/pull/574) | M2.14 UI: remove double Events glyph from overlapping icon templates | `b725268f3224` | `integration/m213k-m214-ssot-stress-20261015` | Draft | #576, #575 | Pending per-PR semantic/CI qualification |
| [#573](https://github.com/masarray/arsas/pull/573) | M2.13K × M2.14: latest Browser reconciliation and SSOT burst/reconnect stress | `d7313f11993c` | `ui/operator-header-six-svg-icons-20261015` | Draft | #574 | Pending per-PR semantic/CI qualification |
| [#572](https://github.com/masarray/arsas/pull/572) | A/B integration: single phasor authority and CF freshness on latest UI stack | `6da4200031d4` | `ui/operator-header-six-svg-icons-20261015` | Draft | — | Pending per-PR semantic/CI qualification |
| [#571](https://github.com/masarray/arsas/pull/571) | UI: exact six operator SVG icons with resize-safe navigation | `15947f4efd18` | `feat/ied-browser-m214-no-scalar-angle-20261015` | Draft | #573, #572 | Pending per-PR semantic/CI qualification |
| [#570](https://github.com/masarray/arsas/pull/570) | M2.14: suppress erroneous angles on scalar MV measurements | `46adacd24ff5` | `feat/ied-browser-m214-sg-value-projection-20261015` | Draft | #571 | Pending per-PR semantic/CI qualification |
| [#569](https://github.com/masarray/arsas/pull/569) | M2.14 SG: show model-verified active setting values in parameter rows | `020e1b027246` | `feat/ied-browser-m214-control-state-evidence-20261015` | Draft | #570 | Pending per-PR semantic/CI qualification |
| [#568](https://github.com/masarray/arsas/pull/568) | M2.14B/C: inventory-proven read-only SGCB and GoCB status inspector | `1d094cfd8ea8` | `feat/ied-browser-m214-ab-vector-parity-20261015` | Draft | #569 | Pending per-PR semantic/CI qualification |
| [#567](https://github.com/masarray/arsas/pull/567) | M2.14 A/B: shared phasor compatibility gate for extra IEC vectors | `94a014430339` | `fix/m2-13h-observed-vector-units-20261015` | Draft | #568 | Pending per-PR semantic/CI qualification |
| [#566](https://github.com/masarray/arsas/pull/566) | Integration A+B: M2.13 SSOT + M2.14 Browser with unified phasor authority | `adca74da9497` | `feat/ied-browser-m2-model-hierarchy-report-qtime-20261014` | Draft | — | Pending per-PR semantic/CI qualification |
| [#563](https://github.com/masarray/arsas/pull/563) | M2.13J: IEC CF/DC evidence, verified engineering units & SSOT reliability | `dde4c74a2562` | `feat/ied-browser-m2-model-hierarchy-report-qtime-20261014` | Draft | #567 | Pending per-PR semantic/CI qualification |
| [#561](https://github.com/masarray/arsas/pull/561) | M2.13F: fence stale SSOT projections on model replacement and exact IEC key identity | `2fadd9869a22` | `feat/ied-browser-m2-model-hierarchy-report-qtime-20261014` | Draft | — | Pending per-PR semantic/CI qualification |
| [#560](https://github.com/masarray/arsas/pull/560) | M2.6B: hierarchical LN/DO/DA/BDA Browser and raw report q/t wire authority | `7a764c3a062a` | `feat/ied-browser-m2-typed-utc-exact-references-20261014` | Draft | #566, #563, #561 | Pending per-PR semantic/CI qualification |
| [#559](https://github.com/masarray/arsas/pull/559) | M2.6A: typed UTC-Time evidence, exact DataSet/Read identities and live provenance | `26b28baad4f6` | `feat/ied-browser-m2-files-selection-provenance-20261014` | Draft | #560 | Pending per-PR semantic/CI qualification |
| [#558](https://github.com/masarray/arsas/pull/558) | M2.5B: selected-files UX, Download reveals folder, source-fenced COMTRADE cache | `c2520cb1440a` | `feat/ied-browser-m2-files-native-workflow-20261014` | Draft | #559 | Pending per-PR semantic/CI qualification |
| [#557](https://github.com/masarray/arsas/pull/557) | M2.5: native Browser Files grid, complete COMTRADE download/open with cancellation | `3f68bd8fe75c` | `feat/ied-browser-m2-quickstart-browse-20261014` | Draft | #558 | Pending per-PR semantic/CI qualification |
| [#556](https://github.com/masarray/arsas/pull/556) | M2.4A: source-neutral Browse IED Quick Start with typed task authority and lazy model landing | `6c96039c5dbd` | `feat/ied-browser-m2-minimal-splitter-20261014` | Draft | #557 | Pending per-PR semantic/CI qualification |
| [#555](https://github.com/masarray/arsas/pull/555) | M2.3A: minimal hover-responsive native WPF IED Browser splitter | `6d3a8bc7c56f` | `feat/ied-browser-m2-schema-index-20261014` | Draft | #556 | Pending per-PR semantic/CI qualification |
| [#554](https://github.com/masarray/arsas/pull/554) | M2.2B: immutable IEC schema index, exact DataSet FC routing and verified DO aliases | `d1864db621cc` | `feat/ied-browser-m2-observation-ledger-20261014` | Draft | #555 | Pending per-PR semantic/CI qualification |
| [#553](https://github.com/masarray/arsas/pull/553) | M2.2A: association-fenced per-IED observation ledger, Report/Read precedence and stale state | `d13b0ee02708` | `feat/ied-browser-m2-typed-quality-20261014` | Draft | #554 | Pending per-PR semantic/CI qualification |
| [#552](https://github.com/masarray/arsas/pull/552) | M2.1: typed Quality evidence and fail-closed MMS bitstring decoding | `76be1df01d1e` | `feat/ied-browser-m1-virtual-tree-detail-20261013` | Draft | #553 | Pending per-PR semantic/CI qualification |
| [#547](https://github.com/masarray/arsas/pull/547) | Browser M1: virtualized two-panel engineering tree and detail presentation | `0a663ec07793` | `feature/p10-9-ied-browser-rcb-exporter-preserved` | Draft | #552 | Pending per-PR semantic/CI qualification |
| [#546](https://github.com/masarray/arsas/pull/546) | docs: ARD for two-panel IED Browser engineering workspace | `d0ee4a64ce6e` | `main` | Draft | — | Pending per-PR semantic/CI qualification |
| [#543](https://github.com/masarray/arsas/pull/543) | P10.9U: targeted CSWI Pos status recovery for truncated MMS NameList | `6285867780b8` | `fix/512-goose-event-header-alignment` | Draft | — | Pending per-PR semantic/CI qualification |
| [#512](https://github.com/masarray/arsas/pull/512) | GOOSE UX: tidy event header alignment without altering capture | `069890df0a00` | `fix/511-canonical-process-values-all-workspaces` | Draft | #543 | Pending per-PR semantic/CI qualification |
| [#511](https://github.com/masarray/arsas/pull/511) | P9: canonical process values and shared badges across all IEC workspaces | `924a00a48c2b` | `fix/510-goose-raw-dataset-inspector-no-quality` | Draft | #512 | Pending per-PR semantic/CI qualification |
| [#510](https://github.com/masarray/arsas/pull/510) | BCUGE operator fix: remove misleading GOOSE Quality column, show exact raw DataSet entries | `c0f4445f0d1c` | `fix/509-bcuge-quality-ied-scope-multigrid-sort` | Draft | #511 | Pending per-PR semantic/CI qualification |
| [#509](https://github.com/masarray/arsas/pull/509) | P8 BCUGE: real Quality column, selected/all GOOSE scope and 3-grid IEC natural sorting | `460e41bed7cf` | `fix/508-goose-operator-inspector-native-sort` | Draft | #510 | Pending per-PR semantic/CI qualification |
| [#508](https://github.com/masarray/arsas/pull/508) | P8: actually wire native IEC natural sort and make GOOSE event/inspector engineer-readable | `bccf18e33cf6` | `fix/507-bcuge-goose-quality-event-natural-sort` | Draft | #509 | Pending per-PR semantic/CI qualification |
| [#507](https://github.com/masarray/arsas/pull/507) | P8 BCUGE: natural telegram order, proven GOOSE quality and one process-event timeline | `57f92d9826de` | `fix/506-goose-quality-events-sntp-persistence` | Draft | #508 | Pending per-PR semantic/CI qualification |
| [#506](https://github.com/masarray/arsas/pull/506) | P8 BCUGE: readable GOOSE quality, semantic event coalescing, inspector UX and persistent SNTP | `fda92c6b1869` | `fix/505-goose-samehost-exact-nic` | Draft | #507 | Pending per-PR semantic/CI qualification |
| [#505](https://github.com/masarray/arsas/pull/505) | P8 C264 P3A: context-aware exact NIC selection for same-PC GOOSE capture | `5e55fa2022bf` | `fix/504-c264-ip-fault-checkbox-alignment` | Draft | #506 | Pending per-PR semantic/CI qualification |
| [#504](https://github.com/masarray/arsas/pull/504) | P8 C264: center editable IED IP and align Fault Records select-all | `f90f4697fb1e` | `fix/501-sntp-header-ux-heartbeat` | Draft | #505 | Pending per-PR semantic/CI qualification |
| [#503](https://github.com/masarray/arsas/pull/503) | Handoff: C264 same-host simulator, GOOSE NIC, reporting evidence and UI regression plan | `4613838f6081` | `fix/501-sntp-header-ux-heartbeat` | Draft | — | Pending per-PR semantic/CI qualification |
| [#502](https://github.com/masarray/arsas/pull/502) | P8 planning: C264 same-host GOOSE NIC intelligence, typed report completeness and UX parity | `46639977ab10` | `fix/501-sntp-header-ux-heartbeat` | Draft | — | Pending per-PR semantic/CI qualification |
| [#501](https://github.com/masarray/arsas/pull/501) | Polish NTP header with ARSAS design tokens, readable PC IP and packet heartbeat | `34d8f55f499b` | `feat/500-standalone-sntp-ip-control` | Draft | #504, #503, #502 | Pending per-PR semantic/CI qualification |
| [#500](https://github.com/masarray/arsas/pull/500) | Standalone NTP Server — local IP dropdown, live toggle and communication LED | `5e46dd562fc1` | `feat/499-goose-engineering-workstation` | Draft | #501 | Pending per-PR semantic/CI qualification |
| [#499](https://github.com/masarray/arsas/pull/499) | GOOSE engineering workstation: typed quality, immutable event inspector, smart NIC capture | `a6d788d789ea` | `fix/498-goose-cta-navigation-adapter` | Draft | #500 | Pending per-PR semantic/CI qualification |
| [#498](https://github.com/masarray/arsas/pull/498) | GOOSE workstation UX: real card CTA, NIC auto-start, quiet messages, 3s changes | `67266f201733` | `integration/495-smart-dataset-closure` | Draft | #499 | Pending per-PR semantic/CI qualification |
| [#497](https://github.com/masarray/arsas/pull/497) | P7.6G: smart DataSet-first Discovery ST/MX semantic closure | `e0d63a9b8cd1` | `fix/493-f650-whole-fcd-phase-values` | Draft | #498 | Pending per-PR semantic/CI qualification |
| [#496](https://github.com/masarray/arsas/pull/496) | P7.7 F650: DataSet-first smart discovery semantic parity with Open SCL | `0bd1d493ebab` | `fix/493-f650-whole-fcd-phase-values` | Draft | — | Pending per-PR semantic/CI qualification |
| [#494](https://github.com/masarray/arsas/pull/494) | P7.6F: restore report-backed whole-FCD WYE/DEL current and voltage phases | `3cf821428efd` | `integration/446-f650-ux-acceptance` | Draft | #497, #496 | Pending per-PR semantic/CI qualification |
| [#492](https://github.com/masarray/arsas/pull/492) | P7.6F acceptance: reconcile F650 command stack with native IED dialog UX | `2d5e65cdd36d` | `integration/446-f650-canonical-nonmodal-shout` | Draft | #494 | Pending per-PR semantic/CI qualification |
| [#491](https://github.com/masarray/arsas/pull/491) | P7.6F integration: canonical F650 commands + nonmodal evidence-based reject shout | `3c6ccc84a9fe` | `fix/446-f650-command-identity-rejection-evidence` | Draft | #492 | Pending per-PR semantic/CI qualification |
| [#490](https://github.com/masarray/arsas/pull/490) | P7.6F #446: canonical CSWI commands and evidence-based rejection shout | `c08273201542` | `feat/446-p7-6e-report-activation-reason-causality` | Draft | — | Pending per-PR semantic/CI qualification |
| [#489](https://github.com/masarray/arsas/pull/489) | G1/F650 #446: canonical IEC 61850 command targets + evidence-graded reject reasons | `0a47560f4784` | `feat/446-p7-6e-report-activation-reason-causality` | Draft | #491 | Pending per-PR semantic/CI qualification |
| [#488](https://github.com/masarray/arsas/pull/488) | P7.6E #446: correlate engine RptEna/GI activation with report inclusion reasons | `d9d11ea2a24f` | `feat/446-p7-6d-engine-optflds-provenance` | Draft | #490, #489 | Pending per-PR semantic/CI qualification |
| [#487](https://github.com/masarray/arsas/pull/487) | P7.6D #446: retain ARIEC-decoded report OptFlds provenance, without MMS reparse | `3f2bc75d9efb` | `feat/446-p7-6c-per-rcb-continuity-snapshot` | Draft | #488 | Pending per-PR semantic/CI qualification |
| [#486](https://github.com/masarray/arsas/pull/486) | UX: legible, compact IED Actions and Add IEC 61850 IED dialogs | `56de3e2e0e82` | `feat/446-p7-6c-per-rcb-continuity-snapshot` | Draft | — | Pending per-PR semantic/CI qualification |
| [#485](https://github.com/masarray/arsas/pull/485) | P7.6C #446: auditable bounded per-RCB continuity evidence in Copy Diagnostic | `8d764128ccf7` | `feat/446-p7-6c-iec-report-continuity-evidence` | Draft | #487, #486 | Pending per-PR semantic/CI qualification |
| [#484](https://github.com/masarray/arsas/pull/484) | P7.6C #446: IEC 61850 report continuity evidence (SqNum/segmentation/EntryID) | `397d7836e5b0` | `fix/446-p7-6b-canonical-member-ordinal-parity` | Draft | #485 | Pending per-PR semantic/CI qualification |
| [#483](https://github.com/masarray/arsas/pull/483) | P7.6B #446: canonicalize SCD/Discovery DataSet member ordinals in parity fingerprint | `5a05ab2187e0` | `feat/446-p7-6b-semantic-diagnostic-evidence` | Draft | #484 | Pending per-PR semantic/CI qualification |
| [#482](https://github.com/masarray/arsas/pull/482) | P7.6B follow-up: copy exact bounded semantic parity basis in diagnostics | `0b84e81547ab` | `feat/446-p7-6b-parity-evidence-integrity` | Draft | #483 | Pending per-PR semantic/CI qualification |
| [#481](https://github.com/masarray/arsas/pull/481) | P7.6B #446: fail-closed Discovery/Open-SCL parity evidence integrity | `6f97f3b51895` | `fix/446-p7-6a-scl-ap-bound-endpoint-provenance` | Draft | #482 | Pending per-PR semantic/CI qualification |
| [#480](https://github.com/masarray/arsas/pull/480) | P7.6A #446: IP-independent SCD AP selection and exact-AP binding provenance | `0a5226b7f3bd` | `fix/446-p7-5-scd-serverat-endpoint-selection` | Draft | #481 | Pending per-PR semantic/CI qualification |
| [#479](https://github.com/masarray/arsas/pull/479) | P7.6 #446: fail-closed field diagnostic qualification intake, no physical promotion | `175c400262d7` | `fix/446-p7-5-scd-serverat-endpoint-selection` | Draft | — | Pending per-PR semantic/CI qualification |
| [#476](https://github.com/masarray/arsas/pull/476) | P7.5 #446: smart SCD ServerAt dual-MMS AP binding in native Studio | `7384de375aad` | `feat/446-p7-4-causal-dual-ingress-traffic-proof` | Draft | #480, #479 | Pending per-PR semantic/CI qualification |
| [#473](https://github.com/masarray/arsas/pull/473) | P7.4 #446: causal dual-ingress BRCB/URCB traffic qualification | `df3f8ebbaf46` | `feat/446-p7-3-static-ingress-parity-proof` | Draft | #476 | Pending per-PR semantic/CI qualification |
| [#471](https://github.com/masarray/arsas/pull/471) | P7.3 #446: prove Discovery/Open-SCL static acquisition parity | `389a6353756c` | `fix/446-p7-2-association-lifecycle` | Draft | #473 | Pending per-PR semantic/CI qualification |
| [#466](https://github.com/masarray/arsas/pull/466) | P7.2 #446: harden association lifecycle and event-driven traffic proof | `ccf692101844` | `fix/446-p7-1-scl-workspace-diagnostics` | Draft | #471 | Pending per-PR semantic/CI qualification |
| [#459](https://github.com/masarray/arsas/pull/459) | P7.1 #446: collapse duplicate SCL cards and harden diagnostics | `6e99b1a4b528` | `feat/446-unified-static-acquisition-consumer` | Draft | #466 | Pending per-PR semantic/CI qualification |
| [#454](https://github.com/masarray/arsas/pull/454) | P7 #446: consume engine canonical static acquisition for Discovery/Open SCL | `271ad92a9865` | `fix/scl-smart-association-424` | Draft | #459 | Pending per-PR semantic/CI qualification |
| [#435](https://github.com/masarray/arsas/pull/435) | build(deps): bump the github-actions group across 1 directory with 2 updates | `b96041228a3f` | `main` | Ready | — | Pending per-PR semantic/CI qualification |
| [#374](https://github.com/masarray/arsas/pull/374) | fix(scl): gate export-only counter correction on exact live integer type | `43893c60239b` | `main` | Ready | — | Pending per-PR semantic/CI qualification |

## All open issues
| Issue | Title | Preliminary handling |
|---|---|---|
| [#730](https://github.com/masarray/arsas/issues/730) | P0 UI/UX — unify premium SMV cool-blue theme across ARSAS; centered normal window startup | Requires evidence-based reconciliation |
| [#651](https://github.com/masarray/arsas/issues/651) | [LOCKED CHECKPOINT] ARSAS ctlModel/Command Dock + IED Card + GOOSE — cross-thread anti-regression baseline | Protected acceptance/checkpoint |
| [#578](https://github.com/masarray/arsas/issues/578) | UI follow-up: finish Material icon inventory in specialized dialogs and legacy P2 search | Requires evidence-based reconciliation |
| [#544](https://github.com/masarray/arsas/issues/544) | P10.9Z: lossless buffered IEC 61850 events and command outcomes in SOE | Requires evidence-based reconciliation |
| [#542](https://github.com/masarray/arsas/issues/542) | CI COST P0 — eliminate duplicate PR waves and repeated Windows builds (P10.9K) | Requires evidence-based reconciliation |
| [#540](https://github.com/masarray/arsas/issues/540) | P10.9K — generic multi-vendor control semantics, safety and lightweight MMS inventory | Requires evidence-based reconciliation |
| [#538](https://github.com/masarray/arsas/issues/538) | P10.9J — restore trusted Open SCL ZIV association and IEC BSC TapChg command semantics | Requires evidence-based reconciliation |
| [#536](https://github.com/masarray/arsas/issues/536) | P10.9I — bounded canonical InformationReport FIFO: qualify engine CodeVerified consumer integration | Requires evidence-based reconciliation |
| [#535](https://github.com/masarray/arsas/issues/535) | P10.9H — IEC 61850-aware BCUGE diagnostics: distinguish same-interface aliases and late BRCB recurrence | Physical/provenance blocker or active defect |
| [#532](https://github.com/masarray/arsas/issues/532) | P10.9G — fail-closed InformationReport subscriber burst overflow, bounded FIFO, exact-engine qualification | Physical/provenance blocker or active defect |
| [#531](https://github.com/masarray/arsas/issues/531) | P10.9F — qualify ARIEC core routing guard as an explicit code-only engine trial (preserve physical R7/R10 gates) | Physical/provenance blocker or active defect |
| [#525](https://github.com/masarray/arsas/issues/525) | CANONICAL HANDOFF — ARSAS P10.9K code-verified multi-vendor control safety / physical retest pending | Protected acceptance/checkpoint |
| [#520](https://github.com/masarray/arsas/issues/520) | P10.8: isolate BCUGE BRCB BufOvfl and SqNum/EntryID discontinuities with same-slot wire-order evidence | Physical/provenance blocker or active defect |
| [#518](https://github.com/masarray/arsas/issues/518) | MILESTONE LOCK: P10.6 BCUGE IED identity + 7-row static parity verified on simulator | Protected acceptance/checkpoint |
| [#495](https://github.com/masarray/arsas/issues/495) | F650 discovery PCAP gap: bounded DataSet-first typed schema closure vs brute-force GetNameList | Requires evidence-based reconciliation |
| [#493](https://github.com/masarray/arsas/issues/493) | F650 Open SCL: expose report-backed MMXU1 A/PhV/PPV phase measurements | Requires evidence-based reconciliation |
| [#446](https://github.com/masarray/arsas/issues/446) | Consume smart interoperability engine and unify Discovery/Open-SCL runtime planning | Requires evidence-based reconciliation |
| [#380](https://github.com/masarray/arsas/issues/380) | Repository independence, provenance and maintainability hardening | Requires evidence-based reconciliation |
| [#341](https://github.com/masarray/arsas/issues/341) | Post-release: finish SCL semantic parity without regressing accepted v1.6.40 | Requires evidence-based reconciliation |

## Decision procedure
1. Fetch fresh PR head/base, review, changed files, CI and engine lock; inspect safety/GUI/SSOT boundary.
2. Prove exact ancestry or semantic replacement; compare parent-specific changes with canonical descendant, including tests and FAT evidence.
3. If fully covered, comment disposition / canonical replacement / unique delta / safety / follow-up before closing PR; never delete its branch.
4. If uncertain, active or safety-gated, **keep open**. PRs #729/#727/#726/#716/#732/#688 are special protected workstreams.
5. Reconcile issues separately; checkpoint #651, #518, #525 and unresolved physical #520 require preservation.
6. Do not mutate stable tags, binaries, main, engine authority, safety gates or expensive CI to reduce backlog.

## Follow-up
Evidence-backed classifications and closed PR manifest will be added as cleanup advances; all open entries remain actionable for verification, not presumed obsolete.
