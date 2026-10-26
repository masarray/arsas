# ARSAS — Principal-Architect Handoff: Safe PR/Issue Reconciliation
**Date:** 2026-10-26 (Asia/Jakarta, morning)
**Repository:** https://github.com/masarray/arsas
**Engine:** https://github.com/masarray/ARIEC61850
**Status:** Real GitHub work in progress. NO declaration of completion. **Do not begin from stale snapshots.**

## 0. FIRST READ — mandatory
1. [Original full audit snapshot](../maintenance/ARSAS_OPEN_PR_ISSUE_AUDIT_2026-10-26.md): 154 initial PRs, 19 initial issues, all item titles/SHA/base branches and initial branch dependency graph. This remains the **immutable initial inventory**.
2. [Verified decisions and ongoing cleanup](../maintenance/ARSAS_RECONCILIATION_DECISIONS_2026-10-26.md): all actual closures, per-PR proof, safety blockers and current continuation notes.
3. [Source promotion PR #729](https://github.com/masarray/arsas/pull/729), [RC consolidation #727](https://github.com/masarray/arsas/pull/727), [engine promotion #192](https://github.com/masarray/ARIEC61850/pull/192), locked [issue #651](https://github.com/masarray/arsas/issues/651), canonical [issue #525](https://github.com/masarray/arsas/issues/525).
4. On starting work, re-fetch *all* open PRs and issues **with pagination**, all relevant PR head/base SHAs, current `main`, latest release/tag, and any comments or commits written by parallel threads.

**Important:** This document is intentionally isolated in a dedicated handoff branch based on the audit checkpoint. The audit branch can continue to advance independently; read the **live** version of the decision log on `audit/arsas-pr-issue-reconciliation-20261026` if it has newer changes.

## 1. Mission and immutable boundaries
Act as Principal Architect, maintainer, release engineer and IEC 61850 safety reviewer. Minimize redundant open PRs/issues **only after individual proof**, preserve every unique source/test/document/evidence delta, and leave a durable audit trail. Perform safe changes on GitHub, not just a report.

- **Never** mutate accepted published stable **v1.6.42**, binary assets, release tag or checksums. No main force push, branch deletion, fake field qualification, or disabled/weakened safety CI.
- Keep `main`, release tag, RC/integration, active feature branches and ARIEC61850 engine trial **distinct**.
- A green Windows build or commit ancestry alone is **not** physical R7/R10 qualification, current-tree semantic equivalence or consent to issue MMS Write/Operate.
- Do not undo operator-accepted GUI, Command Dock, typed Write/readback, SSOT quality, Report/GOOSE/SMV behavior, or SG7/GR_X/ZIV work. Do not interfere with GUI, SG7 performance, SSOT/SMV and Control/Write threads.

## 2. Most recent verified GitHub state (must re-fetch)
- Baseline at task entry: **154 open PR / 19 open issues**.
- **Live handoff snapshot: 133 open PR / 17 open issues**. Exactly 21 historical PRs and 2 code-only milestone issues closed so far. This is not a claim of complete audit or regression proof.
- ARSAS `main` SHA `14e5dddfbaae012b61a021366c1a864d83d316f2`, unchanged from task entry.
- ARIEC61850 `main` `3d21238d0b62f24faecd5948a4ee238c82b35966`.
- Accepted stable `v1.6.42` tag commit `3db2ca98d144ab4b456e8dc1db9af18ad7a948f2`, tree `bc278a81a96517a979a161d2889493047cf204e0`.
- #729/#727 shared RC HEAD `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, tree `257abcdb4f7b184458c4d24ee9e61c3f043c97bb`.
- Stable tag to RC head: 8 commits ahead, 0 behind, 13 changed release/documentation/site/metadata/workflow paths; **the Git trees are not identical**. The #729 body saying identical tree is stale; see [audit comment](https://github.com/masarray/arsas/pull/729#issuecomment-6403437599).
- RC engine lock references trial [ARIEC #192](https://github.com/masarray/ARIEC61850/pull/192) commit `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`; engine `main` is different. No silent repin.
- The checked ARSAS main Windows Build passed, but GitHub API reported `main.protected=false`; do not equate that with the IEC physical safety policy being satisfied.

## 3. Completed work — do not repeat
Historical intermediate PRs closed with **individual disposition comments and preserved branches/commits**:
- First 15: **#498, #499, #500, #501, #504, #505, #506, #507, #508, #509, #510, #511, #571, #574, #575**.
- Next six: **#581, #601, #555, #646, #647, #648**. The six were verified from the **latest audit decision log**, with exact-test/source checks; see the Phase B2/C2 section for detailed proof.
- Closed milestone issues **#536** (engine #166 + ARSAS #537, code-only staging) and **#538** (engine #167 + ARSAS #539, code-only staging). **No physical R7/R10 or BCUGE continuity conclusion.**
- The preserved canonical staged descendants include open **#543** and **#649**. Do not assume these alone are approved for mainline merge. Original closed PR heads remain reachable and their source branches were not deleted.

**Caution:** Earlier closures were based on full commit ancestry plus focused patch/test checks for intermediate milestones, **not** proof that every later runtime behavior is identical. If semantic comparison uncovers an important lost regression or necessary code, reopen or create an explicit corrective integration path instead of concealing it.

## 4. Absolutely protected PR/issue lanes
- **#729** draft source promotion: DO NOT MERGE unless current physical-control/interoperability R7/R10 provenance and all PR-context required checks pass. Stable v1.6.42 remains accepted independently.
- **#727** RC integration, shared head with #729 but different base and purpose; not an automatic duplicate to close.
- **#726** frozen user-accepted MMS Write -> exact readback -> canonical SSOT; preserve SMV side.
- **#716** fail-closed Boolean live Write safety; branch **diverged** from #679, and prior mergeability was false.
- **#732 / issue #730** premium GUI controls; dependent on P0 #731 and operator design acceptance. No layout rollback.
- **#688** SG7_BCU async indexing/performance and CF-aware units, needs 4-IED/FAT verification.
- **#374** still contains a unique SCL export live integer-type guard patch; **do not close**.
- **#435** dependency bump for GitHub workflows; main still has `actions/cache@v5` in a canonical CI workflow; **do not close as integrated**.
- **#546** unique 456-line IED Browser ARD document absent on main at proposed path; preserve or integrate deliberately.
- Protected issues: **#651** locked Command Dock/GOOSE/UI checkpoint, **#518** physically verified simulator milestone, **#525** code-vs-physical control handoff, **#520** BCUGE SqNum/BufOvfl, **#540**, **#544** SOE, **#542** CI cost, **#495/#493** F650, **#446**, **#341**, **#380**, and active UI issues.

## 5. The high-priority unique-delta risk found in Phase B2/C2
These are *not safe to close*:
1. **[#606](https://github.com/masarray/arsas/pull/606)**: original `tests/ARSAS.Tests/M217DFieldEvidenceConvergenceTests.cs` blob `9df525047ac1186ed822a7f2a0cd0f86f811a4a9` is ABSENT from current RC. Seven tests cover GoCB wire identity, ambiguous publishers failing closed, ordered PDU slots, SGCB authority, association-fenced passive subscription. **Map all assertions against present test coverage.** If actually missing, prepare focused *test-only* restoration on a dedicated branch, preserve current semantics and avoid code duplicates. See PR comment #6405354674.
2. **[#615](https://github.com/masarray/arsas/pull/615)**: `Services/Iec61850ControlRequestSnapshot.cs` blob `96277fceb3490a778ab0d7113fadc0bb13edee1a` is ABSENT from RC. Historical purpose was protecting WPF-owned objects from late command-session mutations. Inspect CURRENT command ownership, retired session lifecycle and regressions. **Do not blind cherry-pick**, change command ordering, or authorize live operation. See comment #6405355124.
3. **[#502](https://github.com/masarray/arsas/pull/502)**: two historical C264 field-trial/handoff documents ABSENT from RC, branch diverged. Preserve sanitized provenance; do not publish private network/capture details. Comment #6405355619.
4. **[#503](https://github.com/masarray/arsas/pull/503)**: distinct historical C264 handoff Markdown ABSENT from RC, branch diverged. Preserve until archive/replacement assessed. Comment #6405356204.
5. **#494**: only **4/5** changed files exactly match current RC; `Services/Iec61850StaticDataSetAuthoritySelection.cs` evolved. Explicitly verify whole-FCD WYE/DEL phase selection and no fake scalar/RCB identity before declaring equivalent.

## 6. Partial semantic file audit — unclosed, no inferred decisions
Compare per-PR touched source HEAD tree blobs against RC `817c391...`, using GitHub recursive tree + per-PR patch. **Exact-count ≠ behavioral acceptance.** Representative observed exact matches out of touched files:
- #459 **1/4**; #466 **3/5**; #471 **1/7**; #473 **3/7**; #476 **1/13**.
- #479 **0/4**; #480 **4/8**; #481 **1/2**; #482 **1/2**; #483 **1/2**; #484 **1/3**; #485 **1/5**.
- #486 **0/6**; #487 **1/5**; #488 **1/7**; #489 **0/11**; #490 **0/4**; #491 **0/5**; #492 **0/7**.
- #496 **0/11**, #497 **1/6**. #502 **0/2**, #503 **0/1**, known divergent documents.
- #547 **4/20**, #552 **3/7**, #553 **1/5**, #554 **1/8**, #556 **7/15**, #557 **0/6**, #558 **3/5**, #559 **1/12**.
- #576 **0/6** and is divergent; #579 **2/5**, #580 **3/16**, #582 **2/8**, #583 **0/3**, #584 **1/6**, #585 **1/2**, #586 **1/6**, #587 **1/5**, #588 **0/2**.
- #591 **0/6**, #594 **0/2**, #595 **2/7**, #596 **0/3**, #597 **2/4**, #598 **0/3**, #599 **0/4**, #602 **2/6**, #603 **0/5**, #604 **2/3**, #605 **1/8**; #606 had missing test file.
- #581 (test-only), #601 (native RIO; historical five paths preserved on #649), #555 (splitter) and #646–#648 (UI) were subsequently checked more deeply and closed; see decision log.

These findings **prevent naive mass-close**, but do not mean every source file must remain byte-identical: later correct implementations may have rewritten it. Verify exact acceptance invariants, remaining unique tests, and semantic equivalence *per item*.

## 7. Efficient next actions — safe execution, not a fresh brainstorm
**Step A:** Recheck 100% live pagination & concurrent changes; inspect latest decision log, only remaining open PRs. If the audit branch has progressed, do NOT overwrite its docs or regress counts.

**Step B:** Highest-value investigation: #606 seven unique regression assertions and #615 late-control-request snapshot ownership. Build a short proof matrix: old feature/test → current equivalent (path/method) → evidence → verdict. Keep open where verdict is uncertain.

**Step C:** Historical C264 #502/#503: design sanitized archive pathway that preserves material evidence without conflating planning with shipped code. No release-source contamination or exposure of private endpoint/CID/PCAP.

**Step D:** Triage small, independent documentation/workflow PRs (#546/#435) for real unique value and safe canonical integration. Do not merge unreviewed CI dependency upgrades or create duplicate CI builds just to clean administration.

**Step E:** Continue PR cohorts by **code lineage and semantics**, not PR age: 454–497 P7 interoperability/F650; 547–605 IED Browser/SSOT; 607–645 live authority/command, 646–649 UI; 655–688 upstream/control/FAT; 716–732 current RC. Segregate active owners. Maintain explicit A–G classifications as in original user brief.

**Step F:** Only after exact ancestry, cherry-pick/squash/equivalence, unique-delta, CI/review and safety evidence are established, comment *individually* and close proven superseded/integrated/obsolete PRs. Never close based on title, old red CI, number, base branch or mere shared ancestry.

**Step G:** For safe real code changes, use minimal isolated branch, targeted tests, then canonical required CI when actually merging code. Respect current cross-thread active branches, avoid new redundant Windows builds/PR fan-out (#542). No direct main mutation or physical operation.

**Step H:** Update the *existing* maintenance decision log on its latest branch head, or write a new separate handoff file and link it if simultaneous thread owns that file. Keep an exact trail of SHA, comparator, touched files, test preservation, class, blocker, closure link and next action. Report open PR/issue before→after and what was really executed.

## 8. Per-item classification & comment contract
Class A already integrated/equivalent, B superseded, C overlapping, D valuable unmerged, E blocked physical/safety, F obsolete experiment, G active cross-thread.

Before a PR close, include:
- **Disposition** and canonical replacement commit/PR.
- **Unique delta** and Git ancestry/tree/test/semantic verification.
- **Safety**: why nothing valuable was lost, original source branch not deleted, accepted IEC 61850 + release boundaries intact.
- **Follow-up**: exact actionable successor.
Open PRs should instead have unique value, blocker, dependency, next step, ownership and status **when a grounded change is needed**, avoiding generic repetitive comments.

Issues have independent acceptance criteria. Do not close #651, #518 or unresolved #520 simply because a source PR was administratively closed.

## 9. Definition of done for the continuing thread
Produce **actual GitHub reconciliation**, with links and evidence; minimize CI cost; preserve every unique delta and all physically accepted behaviors. Do not claim full task complete before every remaining PR has been classified. No requirement to drive counters to zero; aim for a faithful, smaller, safer backlog.

## 10. Exact handoff summary
**Continue**, do not restart. First inspect the existing audit and decisions at latest GitHub SHA, then validate #606 and #615, protect divergent #502/#503, and proceed with bounded evidence-driven batch cleanup. At handoff **133 PR/17 issues** were open; live GitHub always wins over this snapshot. Stable v1.6.42 remains immutable and ARIEC trial/physical gates stay closed.
