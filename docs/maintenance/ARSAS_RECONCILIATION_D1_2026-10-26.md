# ARSAS — PR / Issue Reconciliation D1: source lineage and control ownership
**Date:** 2026-10-26 (Asia/Jakarta)
**Purpose:** isolated, reversible checkpoint for the next maintainer; follows [original inventory](ARSAS_OPEN_PR_ISSUE_AUDIT_2026-10-26.md), [verified decisions](ARSAS_RECONCILIATION_DECISIONS_2026-10-26.md), and [prior handoff](https://github.com/masarray/arsas/blob/handoff/arsas-reconciliation-20261026-0701/docs/maintenance/ARSAS_NEXT_THREAD_HANDOFF_2026-10-26.md). This branch is based on audit commit `dd8ad4bcc1c04e436eb501792d7cf878013ce015`, not on `main`. Does not overwrite the parallel audit log.

## 1. Live inventory and immutable boundaries

GitHub REST listings re-fetched with 100/page, including terminating page:
- Entry: **133 open PR / 17 open issues** (excluding PR objects from Issues API).
- After D1 verified bookkeeping: **132 open PR / 17 open issues**, solely historical PR #494 closed.
- Accepted stable **v1.6.42** remains published, tag source `3db2ca98d144ab4b456e8dc1db9af18ad7a948f2`; **no tag/binary/checksum modifications**.
- ARSAS main: `14e5dddfbaae012b61a021366c1a864d83d316f2`, ARIEC61850 main: `3d21238d0b62f24faecd5948a4ee238c82b35966`. No main/engine code changes.
- RC head #729/#727: `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`; separate bases/gates, both drafts.
- Release pin: RC `engines/ARIEC61850.lock.json` historically refers to engine #192 trial head `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`; **do not silently repin** or infer R7/R10 qualification.
- ARSAS main `Build ARSAS` workflow run [40204560933](https://github.com/masarray/arsas/actions/runs/40204560933) completed **success**. Other production workflow outcomes are not a substitute for PR-context CI/physical acceptance.
- This cleanup initiated no Windows builds, no branch deletions, no merges, and no active feature-branch writes.

## 2. Completed D1 mutation: PR #494 (closed unmerged)

[PR #494](https://github.com/masarray/arsas/pull/494), audit [comment 6406021765](https://github.com/masarray/arsas/pull/494#issuecomment-6406021765).

**Disposition B (superseded in integrated RC)**: old head `3cf821428efd954e415e7455acbd2936ff0019cd` is a true ancestor of the protected RC head: GitHub compare old→RC = ahead 1908, behind 0, merge-base old head. **Ancestry alone is not semantic proof**; five changed files independently checked:

| Original changed file | Result vs RC |
|---|---|
| `Services/Iec61850DataSetSignalInventoryService.cs` | identical blob `f3c956350577839c3d4adf4e83ef0c0ac97a88f3` |
| `Services/Iec61850StaticPhaseLeafProjection.cs` | identical blob `2b83b096e3a9de44b17e94bc24585ccbd0abb918` |
| `Services/SchemaSafeAggregateProjectionService.cs` | identical blob `65183fd23793b10230bdeb3b9c913a66c369e794` |
| `tests/ARSAS.Tests/StaticWholeFcdPhaseMeasurementTests.cs` | identical blob `e5089993faa30ddb9c17fc32eaa28ca9cafc8084`; tests 13 WYE/DEL phase magnitudes, metadata q/t, AuxV, idempotence, missing phase/FC fail-closed |
| `Services/Iec61850StaticDataSetAuthoritySelection.cs` | evolved: original `eb94e455f4265b773084396ed37f845a7bf2fd36`; RC `8e9eaf4e5cdb333675584f4c3756c91080f62b14` |

The fifth service uses `device.Signals.ToLookup(NormalizeLiteral(DataSetReference), StringComparer.OrdinalIgnoreCase)` rather than repeatedly scanning `device.Signals.ToArray()` for each FCDA. Existing exact control and WYE/DEL phase-selection predicates, unique match, report-backed membership, runtime publish eligibility and primary-value ranking remain. Semantic equivalence is limited to stable source catalog snapshots; a separate mutation-concurrency audit would be required if upstream ever mutates `device.Signals` during the same Build call. Closure makes **no physical F650 qualification claim**. Maintain issues [#493](https://github.com/masarray/arsas/issues/493), [#495](https://github.com/masarray/arsas/issues/495), performance [#688](https://github.com/masarray/arsas/pull/688), and RC qualification [#729](https://github.com/masarray/arsas/pull/729).

## 3. P0 unique-delta salvage investigation — PR #606

[PR #606](https://github.com/masarray/arsas/pull/606), [new complete 7-method mapping](https://github.com/masarray/arsas/pull/606#issuecomment-6406057666).
- Original unique `M217DFieldEvidenceConvergenceTests.cs` blob `9df525047ac1186ed822a7f2a0cd0f86f811a4a9` is missing from RC; PR old→RC **diverged** (ahead 1236, behind 11), not safe to close.
- Exact GoCB identity/ambiguous publisher: covered by `IedBrowserGooseInspectionTests.ExactCanonicalControlIdentityAndOnePublisherRequired`, `FullyQualifiedWireGoCbCanBeObservedWithoutMmsDataSetDirectory`.
- Exact read-only SGCB initial inventory, no edit-group, absence, session-scoped hydration: covered by `IedBrowserControlStateEvidenceTests.FullLiveInitialHydrationIncludesOnlyVerifiedReadOnlySgcbStatus`, `SgcbReadsOnlyExactInventoryDeclaredReadOnlyMmsLeaves`, `InitialSgcbPlanRejectsSclOnlyAndAmbiguousInventory`.
- RCB nested Trigger/Optional flags: covered by `M217DGooseRcbFilesUxRegressionTests.ReportFlagsStayInsideControlBlockExpandableSection`.
- Subscriber UX has intentionally changed from old `OpenIedGooseSubscriber(device)` to the shared inline `StartGooseSubscriber_Click(sender,e)` on `MainWindow.IedBrowser.cs:913-945` and `BrowserSubscribeIsInlineAndSharesOriginalNpcapStartStop`; do **not** restore obsolete source text tests.
- **Gap 1 requiring tracing**: historical `UnboundWireSlotsRequireCompleteOrderedRealPdu` rejected length mismatch, non-ordered duplicate slots, missing-value sentinel. New `IedBrowserGooseInspection.VerifiedMembers` enforces this for **bound** directory memberships; the **unbound** GUI code `MainWindow.IedBrowser.cs:1706-1758` displays leaves under `GOOSE raw allData · unbound` with no local complete-slot guard. Verify decoder contracts, then restore a minimal guard/regression if not already guaranteed. No claim that a faulty PDU was observed.
- **Gap 2 requiring tracing**: historical SGCB wrong-domain rejection. Current `IedBrowserControlStateEvidence.IsVerifiedInventory` checks verified attributes and self-consistent reference, but initial admission iterates `SettingGroupControls` separately from the model's LD list. Verify cross-model domain identity and add case if absent.
- Association-fenced passive subscriber attribution must be reviewed against present code; old RCB glyph and tab-navigation text assertions are obsolete.
- No blind test port or CI rerun. Preserve PR OPEN until targeted test-only rescue/canonical owner confirms completeness.

## 4. P0 control ownership safety — PR #615

[PR #615](https://github.com/masarray/arsas/pull/615), [new source-level finding](https://github.com/masarray/arsas/pull/615#issuecomment-6406044017).
- Original `Services/Iec61850ControlRequestSnapshot.cs` blob `96277fceb3490a778ab0d7113fadc0bb13edee1a` absent from RC.
- RC/frozen #726/Boolean #716 share same `Services/Iec61850MonitorRuntime.cs` blob `0f783af40b2af82a462b9388e3a6d327188cfa9c` with `session.Client.ExecuteControlAsync(request,...)` at line 1357 and no command copy.
- `MainWindow.xaml.cs:1854+` sets `Signal = signal` from UI/device model; `NativeIec61850Client.cs:1520,1564-1569,1691` mutates `request.Signal` after awaits.
- Timeout/quarantine allows unresolved native operations to terminate late; association-level wire safety is not proof of isolation from stale writes to WPF objects.
- **Verdict: demonstrated alias ownership path; potential late model mutation, not field incident proof.** Preserve #615; owner must design a command-only bounded snapshot / association-fenced publication and focused regression across async stop/reconnect. Do not copy legacy code blindly or weaken fail-closed MMS operation.

## 5. C264 source/evidence preservation

- [#502](https://github.com/masarray/arsas/pull/502) head `46639977ab104b9201dfdd65af0b92f37b06d627`: unique historical documents `docs/field-trials/C264_HANDOFF_PROMPT_2026-10-10.md` and `docs/field-trials/C264_SAME_HOST_REPAIR_PLAN_2026-10-10.md`.
- [#503](https://github.com/masarray/arsas/pull/503) head `4613838f608152a263b54adb84d07f85155a43cf`: `docs/C264_SAME_HOST_FIELD_HANDOFF_2026-10-10.md`.
- Both remain OPEN with branches intact. Original docs are not equivalent to shipped code/field qualification. Assess sanitized archive with sensitive endpoint/PCAP controls before copying content; this checkpoint records names/provenance only.

## 6. Protected active work and next batch

**Do not close/merge solely for housekeeping:** #729 main source promotion with physical R7/R10/engine provenance gates; #727 RC integrated GUI/SSOT/SMV; #726 accepted Write/readback; #716 Boolean safety; #732 GUI; #688 SG7 performance; #374 SCL export; #435 Actions cache upgrade; #546 unique ARD; #649/#543 staged descendants; field issues #520/#525/#651/#518/#493/#495 and CI issue #542.

Next maintainer steps:
1. Fetch current open PRs/issues **with pagination** and head SHAs before any next mutation; this checkpoint is not live truth.
2. Coordinate #615 with MMS Control/Write owner. Add late-session mutable-alias regression and safely isolate request signal ownership on a dedicated code branch; run targeted tests + canonical PR-context CI before any merge.
3. Resolve #606 unbound allData completeness / domain-fence via current decoder and exact tests. Restore minimal test-only assertions if truly absent. Keep old PR until no lost regression.
4. Prepare sanitized C264 archive #502/#503 without leaking field endpoints and without claims of acceptance.
5. Continue historical P7/Browser/RCB cohorts using per-PR ancestry, 100% changed file coverage, tests and semantic behavior. Individual five-section disposition comment **before** administrative closure; preserve original heads/branches. Stop on ambiguity.
6. Preserve main, stable v1.6.42, trial engine pin and all physical qualifications. No unnecessary Windows CI.

**D1 conclusion:** one proven closure; two unique-test/ownership risk lanes now have precise reproducible evidence and stay open. ZERO LOST WORK is the goal, not zero open PRs.
