# ARSAS MASTER ENGINEERING HANDOFF D19 → D20
**Date:** 2026-10-27 WIB. Continues [D18](ARSAS_RECONCILIATION_D18_2026-10-27.md). Owner request: continue meaningful progress, preserve efficiency and cross-thread safety.

## D19 concrete milestone: CI-P4 actual source implementation
- **Code branch (based directly on protected integrated RC; no main/stable mutation):** [`ci/p4-failclosed-optimized-gates-d19-20261027`](https://github.com/masarray/arsas/tree/ci/p4-failclosed-optimized-gates-d19-20261027)
- **Exact candidate HEAD:** `6065c6e8e36d67d1f5488153aa99a01183c3cf0d`; parent RC `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`; ahead 2 commits, behind 0.
- **Change scope (exactly four paths):** `.github/workflows/build.yml` (+51 lines), `.github/workflows/smart-discovery-capture-build.yml` (+4/-2), `scripts/test-ci-workflows.py` (+56 lines), `docs/audits/CI_P4_D19_FAILCLOSED_IMPLEMENTATION_2026-10-27.md`.
- Windows CI preflight: minimal read-only Ubuntu GitHub API list-files check; only pure added/modified `README.md` and `docs/**/*.md` (other than `docs/INTEROPERABILITY_REFERENCE_CONTRACT.md`) can skip. Renames, removals, unknowns, empty lists, ≥3000 GitHub file cap, API errors and mixed PRs force full Windows.
- **Architectural correction to historical #653:** A failed `pr-scope` job once could result in an empty output and accidental Windows skip under equality-to-`true`. New required build-windows gate uses `always()` + `needs.pr-scope.result != 'success' || needs.pr-scope.outputs.needs_windows != 'false'` (and explicit push/manual). A failed preflight **cannot silently skip** the canonical build.
- `Build, test, validate and package Windows application` job remains same name and its exact Git SHA/immutable engine, sealed portable package, `archive: false` direct EXE and source provenance steps unchanged.
- Extra Smart Discovery Field Capture Windows packaging now runs on relevant PR **only with** `ci-field-capture` label and non-draft status, or manual dispatch **after** existing `manual-regression` successful exact-SHA proof. Existing workflow names, path filters, source/engine/evidence constraints remain.
- Added two regression assertions to existing CI P0 tests. The full Python tests have **not been run in a GitHub runner**; branch is intentionally **not** associated with a new PR, avoiding uncontrolled multi-workflow CI fan-out.
- Ran offline production-script-equivalent Node VM tests: **24 cases PASSED** (11 file/API classes; 6 Windows job result/event predicates; 7 Field Capture label/manual state cases). These are **decision-path simulations**, not GitHub Actions execution or physical FAT.
- No GitHub Actions runs for candidate HEAD (API returned total_count=0). No release, lock, WPF, C#, engine, IED or stable modification.

Detailed implementation evidence: [D19 CI P4 engineering notes](https://github.com/masarray/arsas/blob/ci/p4-failclosed-optimized-gates-d19-20261027/docs/audits/CI_P4_D19_FAILCLOSED_IMPLEMENTATION_2026-10-27.md).

## GitHub coordination performed
- Open historical [#653](https://github.com/masarray/arsas/pull/653#issuecomment-6427371723) has D19 code-branch link, fail-open correction and explicit hold conditions. **Not closed, not merged** because its historical branch diverged and candidate is unqualified by hosted CI.
- Open cost issue [#542](https://github.com/masarray/arsas/issues/542#issuecomment-6427373624) updated with code result, conditional proof, 403 branch-protection limitation and next owner acceptance gates.
- Attempts to read main branch protection returned **403 Resource not accessible by integration**; rulesets list returned empty but this is **not proof required checks are absent**. Job-level conditional skip marks check success, unlike workflow-level path-filter skip which can leave required status pending (per GitHub docs). Review required checks before promotion.
- No PR closure D19: count unchanged from D18. Paginated live GitHub shows **99 open PRs / 16 independent issues**, **59 prior historical unmerged closures**. Do not close legitimate backlog only to reduce number.

## Immutable topology
- ARSAS main `14e5dddfbaae012b61a021366c1a864d83d316f2`, ARIEC61850 main `3d21238d0b62f24faecd5948a4ee238c82b35966`; both verified unchanged after D19.
- Integrated RC [#727](https://github.com/masarray/arsas/pull/727)/[#729](https://github.com/masarray/arsas/pull/729) both OPEN DRAFT at `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`; RC not main. Engine trial pin `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`, stable `v1.6.42` unchanged.
- Safety owner drafts #753 `0652f66bed0c72dded20ff547e23e2ef1989341c`, #754 `2aab054b6cf9eb5500b1dce5ef5957eb8ba0232d`, #755 `7d235554225aa0341561b4e887a852fb6fd8e269`, #756 `098ec4abdf7ce8681ec97111f12f3f6151eade78`. Preserve dependencies and real hardware FAT gates.
- Write #726, Boolean #716, SG7 #688, IP GUI #732 belong to parallel implementation owners; no D19 changes.

## D20 next execution, evidence-gated
1. **Qualification of D19 code branch:** obtain repository-owner view of required GitHub checks; verify job name and skipped-job behavior, ensure downstream release and physical gates cannot accept absent exact-SHA artifacts. Run minimal meaningful workflow test suite with a controlled CI budget and avoid repeated full Windows waves. Only then consider creating draft PR to RC base or owner-controlled adoption.
2. Prove selective behavior on representative PR events: docs-only skip, actual source full build, API/preflight failure full build, push/manual full build, unlabeled capture skip, labeled capture exact-SHA success, manual capture success gate. Measure before/after CI minutes, don't estimate as achieved savings.
3. Keep #660 open pending public prerelease policy; #479 intake scripts actually missing from RC; #594/#599 operator RCB differences; #582 GOOSE safety overlap; #520 real BRCB BufOvfl/SqNum/EntryID unresolved. Do not substitute source/CI tests for real IED/physical-wire evidence.
4. Reassess safe historical PR closures only on exact Git ancestry **plus actual behavior and test** (not ancestry alone), leave safety/fat owners intact. Previous D15-D18 documents enumerate blockers.
5. Only after explicit independent acceptance move integrated RC toward release; absolutely no implicit engine repin or blind merge.

**D19 status:** engineering code milestone delivered to GitHub isolated branch; offline predicate proof passed; GitHub-run/required-check review outstanding. No new Windows CI cost or claims of full acceptance.
