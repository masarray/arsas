# P0 Quality SSOT + typed SV — field-retest checkpoint (2026-10-21)

## Immutable engineering chain
- ARSAS P0-R: #652 at `9f3212253aa3cfe7e1629069272f1a8e6517bc71`.
- ARSAS P0-I: #655 at `069a32adfae69d9f59fab72aac5f9ba6709371ea`.
- ARSAS P0 typed SV/quality: #656 at `1314005f7f44bf6995a776b4baeaab581f5b7398` (11/11 workflow green; **physical recheck pending**).
- ARSAS quality hint reconciliation: #657 stacked on #656; use **its own** final exact HEAD Windows artifact, never reuse #656 as #657.
- Engine remains ARIEC61850 PR #184 at exact SHA `9c7cb40364023a7b217e5ec31ab3105ba90142eb`. #184 is a trial; no main merge.
- Preserve field-accepted #649/#650 and #652 regression; do not restart from main or promote into production.

## Actual field evidence, not inferred
ZIVAVR (2026-10-20 22:29 UTC): `ATCC1.TapChg.valWTr.posVal [ST]=6` and independently `ATCC1.TapChg.q [ST]=Good`, Source `Report quality · raw MMS`; `ATCC1.TapChg.t` reported. 8/8 selected static members displayed; 1/8 had quality not supplied in selected-signal projection. Earlier Live Signal UI showed `TapChg Quality Unknown`. This is **not** proof that the IED emitted bad quality, nor proof #657 has already repaired the physical screen.

SIEBCU (2026-10-20 22:33 UTC): `XCBR1.Pos.subEna [SV]=False`, `subVal [SV]=bits(00, unused=6)`, `subQ [SV]=bits(0000, unused=3)`, `subID [SV]=Empty VisibleString (0 characters)`; all 4 are OBSERVED via exact MMS Read. ModelType for `subVal/subQ` is generic `Check` despite the differing IEC bit widths. Exact model-proven DPC + two-bit data is eligible for DPC presentation; 13-bit `subQ` is eligible for Quality presentation. Original BER-derived rendered text must remain immutable in the exported ledger.

SIEBCU `XCBR1.OpCnt.stVal [ST]` is `NOT_OBSERVED_NO_ATTEMPT_EVIDENCE`, not a confirmed read failure. `XCBR1.SumSwARs.units.multiplier [CF]` is `READ_FAILED` with zero-length rendered scalar; **GVA/invoke was not captured** and the server-side reason is unverified. Do not invent multiplier or coerce to 0. These are independent remaining P0 Read issues.

## Acceptance procedure — strictly passive except operator's explicit Read
1. Use the **new PR's exact-SHA** Windows portable artifact; inspect About/Diagnostic App SHA and engine SHA before attempting FAT. Archive the artifact digest alongside CSV, diagnostics and screenshots. Retain prior qualified build for A/B comparison.
2. Connect ZIVAVR through verified IEC MMS association; enable existing configured static BRCB only through operator-initiated monitoring. Observe `ATCC1.TapChg.valWTr.posVal`, separately captured `ATCC1.TapChg.q`, Live Signal `TapChg Quality`, and BRCB association/RCB identity. Expected: model-matched and association-fenced typed raw `q` projects into the same signal; no sibling `q` borrowing.
3. Verify `Good`, and when fixture capabilities permit, `Invalid`, `Questionable`, `Test`, `Substituted`. Only real decoded bits can certify flags. Missing q stays not-supplied/unverified; never synthesize Good.
4. Test report-before-value and value-before-report-quality ordering, wrong FC/DO/sibling, selected signal changed, reconnect/new association, duplicate Report, burst Report. A q-only change must update quality but **must not** mutate process value, control dispatch, SOE sequence, process edge, or introduce MMS polling/read/write.
5. Connect SIEBCU. On XCBR1.Pos perform deliberate bounded `Read DO`/`Read LN`. Expect `subVal` display Intermediate [00] for the captured 2-bit wire value, `subQ` Good and expandable quality details, and `subID` empty string, all preserving canonical raw evidence. Check wrong-width, unknown CDC, wrong FC and vendor raw bit-string remain raw.
6. Explicitly read `XCBR1.OpCnt.stVal` with a new evidence CSV to distinguish admission failure vs actual read failure. For `units.multiplier`, collect native GetVariableAccessAttributes/Confirmed-Read request+response InvokeID and the actual MMS AccessResult/decoder outcome before changing projection. No fabricated units.
7. Keep BRCB `BufOvfl` and nonmonotonic `SqNum` as unresolved **SOE continuity observations**. Neither 8/8 nor 18/18 displayed values nor green CI is event-loss, GI, or replay completeness proof.
8. Attach FAT evidence with explicit PASS/FAIL/UNVERIFIED per IED, model and q type, plus CPU/latency/RCB traffic. NEVER automatically send an Operate/Write/Cancel or change IED parameters as part of this plan.

## Automated regression / guardrails
- #656: DPC SV 00/01/10/11, wrong-width rejects, Quality 13-bit fields, subID empty, wrong FC/CDC and immutable raw ledger; selected exact quality route.
- #657: contradictory inventory hint cannot override the schema's exact companion, wrong reference/FC cannot redirect quality, one-time bounded conflict diagnostic examples. No new MMS I/O, report sweep of all points, timers, sleeping, or UI refresh loops.
- Windows CI and installer/portable smoke are gates; field FAT on two IED types is a separate, still-required gate.

## Exit / promotion
Do not mark physical acceptance until exact-SHA package deployed and operator-provided captures prove value/quality parity, negative isolation, no false SOE, and IEDScout-equivalent typed detail on the tested attributes. Branches and PRs remain draft; no release or main merge without that evidence.
