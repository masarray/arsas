# Static DataSet report-only contract

This document is the acceptance boundary for ARSAS Static DataSet monitoring.

## Acquisition authority

- The imported/live ARIEC Static DataSet membership is the signal-selection source of truth.
- One user-visible runtime row is selected for each exact DataSet membership identity; browsed aliases do not become extra rows merely because they share a DataSetReference.
- Configured static BRCB/URCB plus General Interrogation are the process-value acquisition path.
- Cyclic MMS process polling is disabled in this mode.
- Dynamic DataSet writes are disabled in this mode.
- ARIEC planner fallback candidates are diagnostic evidence only and must never become silent MMS polling.

Manual / Select Signals mode keeps the normal Hybrid acquisition behavior and is intentionally outside this contract.

## Deterministic configured-RCB path

Static DataSet mode does not pass configured SCL reporting through the adaptive Hybrid acquisition planner. The deterministic sequence is:

1. Take the exact DataSet membership and RCB -> DataSet binding from the opened/live SCL model.
2. Verify that the exact configured RCB object exists on the active MMS association.
3. Read the exact live DataSet directory to obtain a non-empty ordered member list. This is report-index mapping evidence, not process-value polling.
4. Reject an explicit SCL/live `DatSet` mismatch; never guess another RCB or DataSet.
5. Install the InformationReport receiver before enabling reporting.
6. Write `RptEna=true`, then request `GI=true` through the normal one-shot MMS control plane.
7. Map report values by ordered DataSet member index and project them through the exact semantic report schema.

Missing reservation metadata is not allowed to cancel an otherwise exact configured static RCB before an actual enable attempt. Conversely, a missing exact RCB, unreadable/empty DataSet directory, or explicit SCL/live binding mismatch fails closed. None of these conditions enables cyclic MMS process-value reads.

## Structured report values

ARIEC may decode one structured Static DataSet member into exact semantic scalar descendants. ARSAS may reconstruct the parent DataSet row only when the live/SCL schema proves every required descendant identity. This projection must fail closed: no positional phase guessing, prefix/fuzzy matching, sibling substitution, or MMS fallback is allowed.

Examples covered by the schema-safe report projection are three-phase THD aggregates such as `ThdA` / `ThdPPV` and demand-energy aggregate values such as `DmdWhMV` when the authoritative model exposes the required scalar leaf.

## Physical acceptance

A Static DataSet physical test passes this contract only when:

1. Live Signal Values contains the exact Static DataSet membership set rather than duplicate `cVal` / `instCVal` aliases.
2. The Acquisition column contains only configured static report acquisition (`StaticBrcb` / `StaticUrcb`) for resolved values; it contains no `MMS polling` rows.
3. General Interrogation can supply the initial report image for schema-safe reportable members.
4. A DataSet with no configured RCB remains explicitly unavailable instead of being sent through Hybrid planning or MMS polling.
5. Unsupported or ambiguous report projection remains explicitly Pending/unresolved instead of silently switching acquisition method.
6. Stop/start/reconnect and FAT attach/detach preserve the same report-only authority without freezing the WPF UI.

This keeps the Static DataSet workflow deterministic and prevents a readability or feature change from regressing the established report-only protocol contract.
