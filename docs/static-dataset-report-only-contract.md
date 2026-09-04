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

## Structured report values

ARIEC may decode one structured Static DataSet member into exact semantic scalar descendants. ARSAS may reconstruct the parent DataSet row only when the live/SCL schema proves every required descendant identity. This projection must fail closed: no positional phase guessing, prefix/fuzzy matching, sibling substitution, or MMS fallback is allowed.

Examples covered by the schema-safe report projection are three-phase THD aggregates such as `ThdA` / `ThdPPV` and demand-energy aggregate values such as `DmdWhMV` when the authoritative model exposes the required scalar leaf.

## Physical acceptance

A Static DataSet physical test passes this contract only when:

1. Live Signal Values contains the exact Static DataSet membership set rather than duplicate `cVal` / `instCVal` aliases.
2. The Acquisition column contains only configured static report acquisition (`StaticBrcb` / `StaticUrcb`) for resolved values; it contains no `MMS polling` rows.
3. General Interrogation can supply the initial report image for schema-safe reportable members.
4. Unsupported or ambiguous report projection remains explicitly Pending/unresolved instead of silently switching acquisition method.

This keeps the Static DataSet workflow deterministic and prevents a readability fix from regressing the established report-only protocol contract.
