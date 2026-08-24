# G2.6 Physical Shadow Verification

## Purpose

This phase sits after the deterministic command-bound A3 proof and before any later `ProductionEligible` decision.

It answers one question only:

> Can the exact `InformationReportProven` dynamic report path remain trustworthy when compared continuously against an independent read-only MMS reference, including across a deliberate reconnect?

A shadow PASS is **not** a production promotion.

## Operator entry point

Select the qualified IED and press:

`Ctrl + Shift + S`

The action is explicit commissioning only. It is not executed automatically by normal monitoring.

## Physical topology

Each phase uses two independent MMS associations:

1. **Report association**
   - exact persisted URCB only;
   - exact persisted G2.4 member sequence only;
   - transactional `TrgOps=dchg` lease;
   - `OptFlds=reason-for-inclusion data-set-name`;
   - GI/integrity/qchg/dupd remain disabled;
   - dynamic DataSet is temporary and cleaned on stop.
2. **Reference association**
   - read-only direct MMS reads;
   - exact same proven member sequence;
   - no RCB/DataSet access or mutation;
   - bounded 250 ms polling while the report phase is armed.

## Two-phase reconnect contract

The collector performs:

1. phase 1 report + polling proof;
2. complete monitor/proof-field/fresh-association cleanup;
3. deliberate teardown/reconnect;
4. phase 2 report re-subscription + independent polling-reference recovery;
5. complete cleanup again;
6. typed ARIEC shadow evaluation.

The READY markers are:

- `G2.6 SHADOW PHASE 1 READY — CAUSE ONE SAFE CHANGE`
- `G2.6 SHADOW PHASE 2 READY — CAUSE ONE SAFE CHANGE`

After each marker, cause exactly one already-approved safe process/status change affecting the proven envelope. The shadow collector itself issues **zero** control commands.

## Evidence rules

Every report observation is bound to the exact DataSet index/member pair already persisted in `RcbActivationProof.MemberReferences`.

Every polling observation is independently read through the second MMS association and is recorded against that same exact index/member pair.

The collector records:

- report values and receive ordering;
- independent polling values;
- exact report sequence number when supplied;
- report-carried quality/timestamp only when ARIEC physically projects those fields from the received InformationReport;
- reconnect attempts and successes;
- report re-subscription after reconnect;
- polling-reference recovery after reconnect;
- bounded dynamic activation-attempt count;
- monitor cleanup, proof-field restore and fresh-association closure.

## Quality / timestamp boundary

The currently proven field envelope may contain scalar primary members such as `CSWI1.Pos.stVal`.

A scalar report member does not automatically prove that its data-object quality and timestamp were transported in the same InformationReport. Therefore this phase deliberately does **not**:

- copy polling quality into a report observation;
- copy polling timestamps into a report observation;
- treat report receive time as the IEC data-object timestamp;
- treat report header `TimeOfEntry` as the member's device timestamp;
- invent missing q/t from companion reads.

ARSAS now pins ARIEC61850 PR #99 (`1efad9a2cdb6b4452b13687bbcd8c7ec41a9e53f`). The strict production-facing acceptance policy requires actually observed paired report/poll quality evidence and actually observed paired report/poll device timestamp evidence. If the physical report envelope does not carry them, the gate remains fail-closed. That result is useful field evidence, not a software failure to be bypassed.

## Acceptance layers

The collector separates three outcomes:

1. **Physical collection completed** — both phases, cleanup and reconnect finished.
2. **Typed shadow passed** — ARIEC exact identity/value/q/t/order/missing/duplicate/reconnect/mutation-loop checks all passed.
3. **Production-acceptance candidate** — additionally includes independent Smart Control and static-reporting regression inputs.

`Ctrl + Shift + S` passes those independent control/static inputs as `false`; it never assumes unrelated regressions passed. A later explicit gate must supply reviewed evidence if those regressions are to become true.

## State invariant

This phase never calls `DynamicReportQualificationProfileStore.SaveAsync` and never calls `MarkProductionEligible`.

The persisted profile remains:

`InformationReportProven`

and production automatic dynamic reporting remains:

`OFF`

until a later, separately reviewed promotion gate is implemented and physically justified.
