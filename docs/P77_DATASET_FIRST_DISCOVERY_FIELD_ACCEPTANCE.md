# P7.7 · DataSet-first discovery parity trial

**Status:** engine code/CI verified; ARSAS field acceptance pending. This change is not a complete full-model SCL parity claim.

## What changes for the operator

Connection and Discovery remain one-click. ARSAS first finds DataSets and report controls, then automatically reads the type descriptions needed for those exact reporting signals. The operator is not asked to choose MMS request budgets or manually browse 20,000 signal names.

If the IED contains signal families beyond the fast namespace slice, report-owned `CSWI`, `eveGGIO`, `MMXU` and other verified LN roots are still eligible for schema probes. The displayed phase measurements and control identities stay unchanged. Technical detail remains available in Diagnostics, not as an interruption to the normal workflow.

## Protocol guardrails

- ARIEC61850 is the sole authority for schema interpretation and source evidence.
- Exactly one association-scoped bounded GVA LN→DO→leaf ladder; no second bulk GetNameList.
- Only successful live DataSet directory memberships on observed MMS domains are admitted.
- Default additional LN budget 48; member hints 2048, deduplicated by exact MMS reference.
- The 64-page initial namespace cap and negotiated concurrent-request window remain unchanged.
- No cyclic MMS process polling, no dynamic DataSet writes, no automatic breaker operation.
- Original positional member identity, per-DataSet RCB and report causality remain intact.

## Field qualification (must use same IED state)

1. Open SCL and IP Discovery against the same endpoint; both use Static DataSet mode and 6/6 configured RCB traffic.
2. Compare **exact leaf references** as well as totals: `eveGGIO1.Ind*.stVal` (30), `CSWI1..16.Pos.stVal` (16), 13 `MMXU1.A/PhV/PPV` magnitude leaves and scalar `AuxV`. All present only if actually schema-proven; no fabricated values.
3. Confirm quality, actual values and timestamps, not just row presence; `BufOvfl` / SqNum findings are a distinct continuity gate.
4. Compare number and total bytes of MMS requests and elapsed time using a fresh separate connection. Avoid comparing loopback timings as a physical Ethernet benchmark.
5. Optional comprehensive engineering scan is a separate future capability; this change specifically targets **report-member semantic completeness** while preserving fast discovery.

The first F650 comparison had Open SCL 67 values, IP Discovery 24, with 124/124 DataSet memberships and six RCBs routed. Fixing the omitted DataSet-backed typed LN roots is a hypothesis backed by the user's PCAP audit; it still requires the follow-up physical capture to prove acceptance.
