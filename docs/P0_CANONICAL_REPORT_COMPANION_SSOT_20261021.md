# P0 Report companion → canonical process SSOT checkpoint — 2026-10-21

## Reproduction from user-supplied runtime diagnostics
- The field application version `f5c5c8ef14af1ce8d8f570a3873ff7926abd4443` is a GitHub merge of exact #657 `81cde9e0bd2f9bc822e737ed2954b758eb8c2bdc`; compare is ahead by one merge commit, with **zero file differences**. Do **not** dismiss field feedback as an old EXE.
- Engine remains locked at ARIEC61850 #184 `9c7cb40364023a7b217e5ec31ab3105ba90142eb`.
- ZIVAVR, endpoint 192.16.1.177:102, selected 8 DataSet points on static BRCB brcb01. Actual Report ingress proven, 8/8 process values visible; `ATCC1.TapChg.valWTr.posVal=6` with Live Signal quality Unknown and timestamp '-'; 1/8 quality not supplied in displayed projection.
- BRCB initial `BufOvfl=1`, 13 frames, SqNum omitted by OptFlds (not an independently proven event loss), EntryID present. GI was requested but reason field unavailable; do not claim SOE continuity/GI causality.
- Exact q/t raw bits from **this particular new FAT** have NOT been attached: do not assert whether initial decoding/extraction or companion routing succeeded merely from process value availability. A prior capture had q=Good; each FAT run must be individually verified.

## Root cause found in Consumer source
- `CanonicalPointSnapshotAuthority.Project` intentionally re-reads canonical process Report channel value/quality/time and can override the newest runtime snapshot's metadata.
- Previous #656/#657 `Services/Iec61850MonitorRuntime.cs` published raw q/t **only as separate companion leaves** and called `ApplyValueUpdate` with metadata-only q on `RuntimePointState`. The **process Report** ledger entry still lacked that proven q/t. WPF would re-project its former Unknown/time '-'. This is a confirmed independent SSOT projection defect.
- #658 updates one existing ledger atomically: `MergeVerifiedReportCompanionForAssociation` attaches typed model-owned q/t to an *existing* Report process observation only after exact schema/FC/association and wire-evidence checks. Raw q/t remains on its own canonical ledger leaf for byte/provenance auditing. A value-only report in the SAME association preserves actual previously typed metadata; new associations clear all.
- #658 indexes both q and t owners, never accepts a sibling, fake vendor leaf, wrong FC or old association. `ApplyValueUpdate(...,hasProcessValue:false,trustReportEdge:false)` notifies UI without a synthetic SOE edge. Changed textual q/t invalidates contradicting old typed flag provenance.
- Add bounded `REPORT_COMPANION_TRIAGE` once at first received slice and again at first proven process-q join, with counts `typedRawQ/typedRawT`, `qLedgerAccepted`, `qOwners`, `processQJoined/processTJoined`. This distinguishes extraction vs schema/route vs canonical join failure without per-frame logging, UI refresh or new MMS requests.

## Next exact-SHA gates
1. Check PR #658 exact HEAD Windows 10/10 workflow, full app+engine regressions, native bridge and portable smoke. Each new code update creates a new HEAD and supersedes prior CI/artifact results.
2. FAT only on #658 exact compiled EXE: record app+engine SHA and artifact hash; connect same configured ZIVAVR RCB; capture raw q/t via Browser evidence plus `REPORT_COMPANION_TRIAGE` counters, observed `TapChg` value/quality/time, and event counts.
3. If `typedRawQ=0` or `typedRawT=0`, investigate `IedBrowserReportWireEvidence.Extract`, strict GetVariableAccessAttributes declaration order and `IedBrowserTypedWireCompanionLayout.Resolve/TryLocate` against the actual reported FCD payload. Do not infer q from a success label or create a depth-first heuristic.
4. If `typedRawQ>0` but `qLedgerAccepted=0`, check exact DataSet scope/FC/model freshness; if accepted but `qOwners=0`, check selected leaf's exact schema ownership; if owner exists but `processQJoined=0`, check Report process ledger admission and current association. Each needs independent positive and negative regression before acceptance.
5. Keep `SumSwARs.units.multiplier` GVA/invoke and `OpCnt.stVal` no-attempt evidence open. P0-C 400ms SBOw observation gate remains unchanged and unqualified for speed.
6. No main merge, release, unplanned MMS command, control edit, extra cyclic polling, dynamic DataSet mutation, or claim of IED physical acceptance until on-wire and WPF FAT agree.

See `AGENTS.md`, `docs/WORKSTREAM_COORDINATION.md`, and the prior P0 Read/Command + Quality FAT checkpoints.
