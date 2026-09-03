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
   - exact live `q` and `t` companion objects are read independently when they exist and resolve under the same functional constraint;
   - at most one `q` read and one `t` read are attempted per successful primary-value observation;
   - companion values are decoded with the ARIEC IEC 61850 quality/timestamp decoders;
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
- polling quality/timestamp only when exact live companion objects can be resolved, read and decoded on the isolated polling association;
- reconnect attempts and successes;
- report re-subscription after reconnect;
- polling-reference recovery after reconnect;
- bounded dynamic activation-attempt count;
- monitor cleanup, proof-field restore and fresh-association closure.

## Quality / timestamp boundary

The currently proven field envelope may contain scalar primary members such as `CSWI1.Pos.stVal`.

For the independent polling authority, ARSAS derives only bounded known IEC data-object sibling paths. For example:

`AA1C1F08R4Q0/CSWI1.Pos.stVal`

maps to the independently discovered/read companions:

- `AA1C1F08R4Q0/CSWI1.Pos.q`
- `AA1C1F08R4Q0/CSWI1.Pos.t`

These companions are accepted only when the live MMS directory resolves the exact reference under the same functional constraint. A read failure, missing object or decoder failure remains missing evidence.

A scalar report member still does not automatically prove that its data-object quality and timestamp were transported in the same InformationReport. Therefore this phase deliberately does **not**:

- copy polling quality into a report observation;
- copy polling timestamps into a report observation;
- copy report quality/timestamp into the polling observation;
- treat polling host read time as the IEC data-object timestamp;
- treat report receive time as the IEC data-object timestamp;
- treat report header `TimeOfEntry` as the member's device timestamp;
- invent missing q/t when either independent side does not physically supply them.

ARSAS pins ARIEC61850 PR #99 (`1efad9a2cdb6b4452b13687bbcd8c7ec41a9e53f`). The strict production-facing acceptance policy requires actually observed paired report/poll quality evidence and actually observed paired report/poll device timestamp evidence. Independent polling companion reads close the polling-side evidence gap, but they do not weaken the report-side requirement: if the physical InformationReport does not transport q/t, the strict gate remains fail-closed. That result is useful field evidence, not a software failure to be bypassed.

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
