# ARSAS PR/Issue Reconciliation — Verified Decision Checkpoint (2026-10-26)

> Companion to [full inventory](ARSAS_OPEN_PR_ISSUE_AUDIT_2026-10-26.md). **Latest verified status: 133 open PRs, 17 open issues (21 PRs and 2 issues reconciled from original baseline).** This records what was actually changed, why, and what MUST remain open. It is **NOT** a claim that the entire backlog is reconciled. No main/release/engine source modification in this cleanup.

## Phase C1 before → after (historical checkpoint; current Phase C2 below)
| Metric | Baseline | Verified after cleanup | Delta |
|---|---:|---:|---:|
| Open PRs | 154 | 139 | -15 |
| Open issues (excluding PRs) | 19 | 17 | -2 |
| Published stable | v1.6.42 | v1.6.42 | unchanged |
| Main HEAD | `14e5dddfbaae012b61a021366c1a864d83d316f2` | same | unchanged |

All 15 closed PRs received an **individual audit-trail comment before closing**. They are historical *stacked superseded tracking PRs*, **not** claims of integration into main. For each, compare to the preserved **OPEN** descendant yielded ahead>0 and behind=0 (exact original commit ancestry); parent patch file/tests were inspected. No branch deletions; final integrated behavior still subject to independent semantic and FAT verification.

## Closed intermediate PRs — verified ancestry
| PR | Distinct scope | Preserved open descendant | GitHub compare evidence |
|---|---|---|---|
| [#498](https://github.com/masarray/arsas/pull/498) | GOOSE navigation/inspector first UX | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 258 behind 0 |
| [#499](https://github.com/masarray/arsas/pull/499) | GOOSE engineering workstation | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 249 behind 0 |
| [#500](https://github.com/masarray/arsas/pull/500) | Standalone SNTP server | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 240 behind 0 |
| [#501](https://github.com/masarray/arsas/pull/501) | SNTP header heartbeat | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 237 behind 0 |
| [#504](https://github.com/masarray/arsas/pull/504) | IP-connect and fault UI | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 233 behind 0 |
| [#505](https://github.com/masarray/arsas/pull/505) | GOOSE same-host NIC binding | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 232 behind 0 |
| [#506](https://github.com/masarray/arsas/pull/506) | GOOSE timeline and SNTP persistence | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 231 behind 0 |
| [#507](https://github.com/masarray/arsas/pull/507) | GOOSE event correlation and IEC ordering | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 230 behind 0 |
| [#508](https://github.com/masarray/arsas/pull/508) | GOOSE inspector and natural sorting | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 227 behind 0 |
| [#509](https://github.com/masarray/arsas/pull/509) | GOOSE IED scope | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 225 behind 0 |
| [#510](https://github.com/masarray/arsas/pull/510) | GOOSE raw DataSet UX correction | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 223 behind 0 |
| [#511](https://github.com/masarray/arsas/pull/511) | Shared canonical process values | [#543](https://github.com/masarray/arsas/pull/543) | ancestor, ahead 221 behind 0 |
| [#571](https://github.com/masarray/arsas/pull/571) | Operator icon/navigation resize | [#649](https://github.com/masarray/arsas/pull/649) | ancestor, ahead 611 behind 0 |
| [#574](https://github.com/masarray/arsas/pull/574) | Single Events glyph | [#649](https://github.com/masarray/arsas/pull/649) | ancestor, ahead 605 behind 0 |
| [#575](https://github.com/masarray/arsas/pull/575) | Material icon resources and tests | [#649](https://github.com/masarray/arsas/pull/649) | ancestor, ahead 596 behind 0 |

## Closed fully code-verified staging-scope issues
- [#536](https://github.com/masarray/arsas/issues/536): P10.9I staged code task completed; consumer [#537](https://github.com/masarray/arsas/pull/537) and ARIEC [#166](https://github.com/masarray/ARIEC61850/pull/166) verified merged. This **does not close** #520 BRCB continuity or assert physical acceptance.
- [#538](https://github.com/masarray/arsas/issues/538): P10.9J code-only staging completed; consumer [#539](https://github.com/masarray/arsas/pull/539) and ARIEC [#167](https://github.com/masarray/ARIEC61850/pull/167) verified merged. Physical ZIV/Open-SCL/BSC safety remains in #525/#540.

## Protected PRs and blockers
- [#729](https://github.com/masarray/arsas/pull/729): **DO NOT MERGE**; main promotion physically gated by R7/R10/reference/engine trial pin. Audit comment [tree correction](https://github.com/masarray/arsas/pull/729#issuecomment-6403437599) explains changed post-release metadata. PR author body statement about exact Git tree equality is outdated.
- [#727](https://github.com/masarray/arsas/pull/727): same head as #729 but different integration target; draft RC required for SSOT/GUI/SV staging. Preserve.
- [#726](https://github.com/masarray/arsas/pull/726): frozen field-accepted MMS Write plus exact SSOT readback, 10-file integration rehearsal. Protect from SMV regression.
- [#716](https://github.com/masarray/arsas/pull/716): Boolean live evidence Write safety; PR is not mergeable at latest read, and #679→#716 diverged. Preserve.
- [#732](https://github.com/masarray/arsas/pull/732): compact GUI component work depends on #731/issue #730; active cross-thread UX. Preserve.
- [#688](https://github.com/masarray/arsas/pull/688): SG7_BCU indexing/cache and CF units; needs multi-IED physical/performance qualification. Preserve.
- [#649](https://github.com/masarray/arsas/pull/649), [#543](https://github.com/masarray/arsas/pull/543): remain OPEN as preserved descendants of administratively consolidated PR lineages. These are NOT automatically safe for mainline merging.
- [#374](https://github.com/masarray/arsas/pull/374): SCL live-type export correction has 3 source/test files and diverges from main; unique safety-sensitive work, preserve.
- [#435](https://github.com/masarray/arsas/pull/435): GitHub Actions cache v5→v6 bump in seven workflow paths; current main still contains `actions/cache@v5` in canonical _ci-p1 workflow. Not equivalent/integrated; preserve pending CI/security review.
- [#546](https://github.com/masarray/arsas/pull/546): new 456-line ARD IED Browser engineering document. Verified **absent** from main at that path; preserve, do not close as duplicate.

## Exact release/source integrity findings
- Tagged `v1.6.42`: commit `3db2ca98d144ab4b456e8dc1db9af18ad7a948f2`, Git tree `bc278a81a96517a979a161d2889493047cf204e0`.
- Current #729/#727 RC head `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, Git tree `257abcdb4f7b184458c4d24ee9e61c3f043c97bb`.
- GitHub compare tag...RC: 8 commits ahead, 0 behind; 13 modified release/docs/site/workflow paths, no application-code file paths in that comparison. Therefore **entire trees are NOT identical**, despite the outdated PR description. Accepted stable assets/tag untouched; metadata reconciliation is separate from physical engine qualification.
- RC engine lock points at draft [ARIEC #192](https://github.com/masarray/ARIEC61850/pull/192) `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`, whereas ARIEC main is `3d21238d0b62f24faecd5948a4ee238c82b35966`. Do not silently repin or infer physical authority from 1,269 code tests.
- Latest ARSAS main `14e5dddfbaae012b61a021366c1a864d83d316f2` Build ARSAS workflow was **success**. GitHub branch API reports `main.protected=false`; do not treat this as evidence that all safety/physical required-gate policy is configured/enforced. No workflow protections changed by this cleanup.

## Remaining 17 open issues — explicit reason and next action
| Issue | Handling | Next action |
|---|---|---|
| [#730](https://github.com/masarray/arsas/issues/730) | Active GUI/UX | Complete premium cool-blue control/dialog consolidation on accepted RC visual language; review #732 and preserve operator WPF experience. |
| [#651](https://github.com/masarray/arsas/issues/651) | Locked engineering checkpoint | Preserve exact user-accepted command/GOOSE/UI baseline, field evidence and cross-thread anti-regression references. |
| [#578](https://github.com/masarray/arsas/issues/578) | Feature backlog — UI | Inventory secondary dialogs, migrate visible inconsistent icons with provenance and DPI/layout validation; do not infer primary icon work remains undone. |
| [#544](https://github.com/masarray/arsas/issues/544) | SOE feature/physical qualifier | Implement buffered BRCB ingress provenance and independent command outcome record, require wire causal evidence; do not assert lossless replay from SqNum alone. |
| [#542](https://github.com/masarray/arsas/issues/542) | Active CI cost defect | Assess fan-out and event replays; consolidate canonical exact-SHA Windows evidence without bypassing required guards. |
| [#540](https://github.com/masarray/arsas/issues/540) | Control safety/qualification | Keep generic multi-vendor ctlModel/typed BSC controls fail-closed and obtain separate physical qualification before release/main. |
| [#535](https://github.com/masarray/arsas/issues/535) | Active BCUGE diagnostics | Validate same-interface aliases and late report discontinuity without hiding true loss/BufOvfl. |
| [#532](https://github.com/masarray/arsas/issues/532) | Code/physical blocker | Preserve fail-closed bounded canonical InformationReport FIFO and separate external BRCB causal investigation #520. |
| [#531](https://github.com/masarray/arsas/issues/531) | Engine code-only qualification | Retain provenance of ARIEC routing guard and correct staged pin; physical R7/R10 gate remains independently required. |
| [#525](https://github.com/masarray/arsas/issues/525) | Protected cross-thread handoff | Maintain chronological trial engine/consumer authority, hashes, inherited tests and pending ZIV/BCUGE physical retest. |
| [#520](https://github.com/masarray/arsas/issues/520) | Active field defect | Prove BRCB BufOvfl/SqNum/EntryID issues with same-slot wire ordering, no fabricated replay/lossless claims. |
| [#518](https://github.com/masarray/arsas/issues/518) | Locked accepted milestone | Preserve P10.6 simulator acceptance and exact staging SHA provenance, without extending result to untested hardware. |
| [#495](https://github.com/masarray/arsas/issues/495) | Active F650 interoperability | Resolve capped GetNameList pagination and typed DataSet-first closure with explicit complete/incomplete evidence. |
| [#493](https://github.com/masarray/arsas/issues/493) | Active F650 report-backed values | Prove WYE/DEL A/PhV/PPV phases and quality/timestamps with actual typed report references. |
| [#446](https://github.com/masarray/arsas/issues/446) | Architecture program | Converge Discovery/Open-SCL runtime onto one ARIEC engine-owned semantics layer; no duplicate consumer protocol logic. |
| [#380](https://github.com/masarray/arsas/issues/380) | Active provenance maintenance | Continue Git-tracked attribution, independent clean provenance and tracked acceptance; preserve code and third-party licensing. |
| [#341](https://github.com/masarray/arsas/issues/341) | Post-release SCL correctness | Preserve v1.6.40 accepted behavior while verifying export semantic parity and live type evidence before integration. |

## Unresolved work; not represented as done
1. Complete semantic diff, review and exact-head CI classification for every remaining open PR; the baseline inventory documents all 154 original items. **No remaining PR should be closed solely because its base branch references a newer branch.**
2. Verify #729 gates, compare all release metadata deltas against main, and keep physical operator acceptance separate from source promotion. Do not touch already published v1.6.42.
3. Form canonical lanes by field work: IEC Control/Write; SSOT & Discovery; SG7/GR_X/ZIV; SMV/GOOSE/SOE; GUI; CI; engine. Evaluate unique delta in final tree, not just history, before each additional closure.
4. Reconcile outstanding issues on concrete evidence. Locked checkpoints remain retrievable, and historical CodeVerified milestones may close only with linked validated merge/CI evidence.
5. No new PR created, no Windows build triggered for administrative closure, no force push, no branch deletion, no safety gate weakened.


---

## Phase B2 / C2 — verified continuation (26 October 2026)

> **Current live tally:** 154 initial → **133 open PRs** (21 closed total); 19 initial → **17 open issues** (2 closed total). This supersedes the earlier 139/17 snapshot without rewriting its historical disposition proof. No code changed on `main`, stable v1.6.42 or ARIEC61850. All six additional PR closures were individually commented and left their source branches intact.

### Six additional safe closures with evidence

| Closed PR | Disposition | Proof relative to protected RC `817c3910e8223fa2ac531a4426ada5d2d4bbbe50` |
|---|---|---|
| [#581](https://github.com/masarray/arsas/pull/581) | Integrated/superseded test-only | Only changed test `IedBrowserObservedVectorM213HTests.cs` has exact blob `4091e9b156b5352d00c0b922a69d11b7f966d5f5` in #649 and RC; verified ancestor #649 ahead 587 / behind 0. IEC Vector.ang degree-unit assertion preserved |
| [#601](https://github.com/masarray/arsas/pull/601) | Integrated/superseded RIO compatibility | All five changed files have **exact blobs in #649** (four production files and six-method test suite). Four production files remain exact in RC; regression adapts legacy two-file CFG/DAT expectation to new engine native three-file CFG/DAT/RIO and adds bridge-idempotence assertion. Ancestor #649 ahead 482 / behind 0 |
| [#555](https://github.com/masarray/arsas/pull/555) | Superseded native Browser splitter UX | Only touched `MainWindow.xaml`; RC retains native direct-child `GridSplitter`, 8px drag hitbox, 1px/3px hover-only passive overlay, `ShowsPreview` and `PreviousAndNext`. Ancestor RC ahead 1530 / behind 0; no old WPF code reverse-merged |
| [#646](https://github.com/masarray/arsas/pull/646) | Superseded Command Dock icon checkpoint | Original historical checkpoint document and both icon regression tests have **identical blobs in RC**; `CommandDockTransparentHeaderExpander` remains defined in App.xaml and bound in MainWindow.xaml. Ancestor RC ahead 807 / behind 0 |
| [#647](https://github.com/masarray/arsas/pull/647) | Superseded distinct discovery/Browser Read progress | Both regression test files have **identical blobs in RC**; current `BrowserReadProgressLine`, computed `IedCardProgressPercent` and `IsDiscoveryProgressActive`-only easing remain. Ancestor RC ahead 798 / behind 0 |
| [#648](https://github.com/masarray/arsas/pull/648) | Superseded integrated selected-IED card rail | Exact RC checkpoint document and regression test; current `P2MainIedListItemStyle` uses one rounded card silhouette with `SelectionRail` directly on the left border, `IsHitTestVisible=False` and no detached margin. Ancestor RC ahead 793 / behind 0 |

**Important:** Surviving *specific semantics* + exact regression assets are verified where production files evolved; the whole runtime tree was **not** asserted identical. All original commits remain reachable; current RC remains DRAFT and physically gated.

### Protect, DO NOT close: concrete unique-delta gaps identified

1. [#606](https://github.com/masarray/arsas/pull/606#issuecomment-6405354674): historical test file `tests/ARSAS.Tests/M217DFieldEvidenceConvergenceTests.cs` at blob `9df525047ac1186ed822a7f2a0cd0f86f811a4a9` is **absent in current RC**. Seven tests cover exact GoCB wire identity, ambiguous publisher fail-closed behavior, real ordered PDU slots, SGCB inventory/FC authority, and association-fenced passive subscribe. It may be reimplemented elsewhere; match all assertions before closing.
2. [#615](https://github.com/masarray/arsas/pull/615#issuecomment-6405355124): historical service `Services/Iec61850ControlRequestSnapshot.cs` (blob `96277fceb3490a778ab0d7113fadc0bb13edee1a`) is **absent in current RC**. Its original role was copying command-only requests so retired MMS sessions cannot mutate WPF-owned signal models. A replacement is unproven: review command ownership/late-response controls and focused regression tests, not a blind cherry-pick.
3. [#502](https://github.com/masarray/arsas/pull/502#issuecomment-6405355619): two historical C264 same-host field-trial/strategy Markdown files are **absent in RC** and the PR is **diverged**, not a clean ancestor. Keep open until an appropriate sanitized archive/equivalent is identified; plan documents are not deployed code or physical FAT acceptance.
4. [#503](https://github.com/masarray/arsas/pull/503#issuecomment-6405356204): distinct `docs/C264_SAME_HOST_FIELD_HANDOFF_2026-10-10.md` is **absent in RC**, branch diverged. Keep historical evidence and specific scope separate from release authority.

Additional checked but **not safe to close yet**: #494 has 4 of 5 changed paths byte-identical to RC (one field-critical static-selection service evolved); #459/#466/#471/#473/#476 and #481–#488 have unchanged regression assets but substantial evolving runtime paths. #574/#575 were previously closed with preservation; #576 is divergent and remains open. Do not classify as redundant merely because the later app builds.

### Updated continuation priorities

1. For missing #606 tests, map each of its 7 assertions to currently compiled test methods, and make an isolated, minimal test-only proposal only if true coverage is missing.
2. For #615, establish current per-command object ownership and late-reply behavior before any code port; keep all MMS Write authority and physical-qualification gates intact.
3. Preserve the historical C264 field-trial documents in #502/#503 unless there is an explicit sanitized, semantically complete canonical successor. Do not expose private/simulator network details in public cleanup summaries.
4. Continue a bounded, per-PR semantic delta matrix. Prefer exact ancestry + blob identity + regression/behavior equivalence. Stop on ambiguous physical, safety, engine-pinned or active GUI work.
5. At time of this checkpoint, no Windows Actions reruns or redundant PR builds were initiated by cleanup.
