# G2.6-P1 — Deterministic Command-Bound A3 dchg Proof

## Goal

Convert the previous generic/manual G2.5-A dchg stimulus into one deterministic ARSAS-owned evidence chain:

`existing ARSAS control command -> qualified MMS status transition -> Dynamic URCB InformationReport(reason=data-change) -> cleanup`

This is a commissioning proof only. It does **not** mark an IED `ProductionEligible` and it does **not** enable production automatic dynamic reporting.

## Entry point

Select the target IEC 61850 IED in ARSAS, then press:

`Ctrl + Shift + A`

The older A2.1 read-only command witness remains available separately on `Ctrl + Shift + F`.

## Preflight gates

Before the report path is allowed to mutate an RCB, P1 requires:

1. the persisted profile is identity-compatible and exactly `InformationReportProven`;
2. the G2.4 RCB activation proof and InformationReport proof are successful;
3. the exact G2.4 member sequence still resolves on the live IED;
4. at least one existing ARSAS control object exposes an exact `ControlStatusReference`;
5. the A2.1 status/focus chain for that command intersects the exact G2.4-proven DataSet member sequence;
6. no control command is already busy.

If the command/status chain does not intersect the qualified DataSet, A3 stops **before** the core report transaction is started. The operator is told to re-qualify an envelope containing the relevant CSWI/XCBR status instead of spending a breaker operation on an unprovable stimulus.

## Armed transaction

The existing `DynamicReportSpontaneousDataChangeCommissioningService` remains authoritative for the report transaction:

- one exact G2.4-proven URCB;
- one bounded temporary dynamic DataSet;
- `TrgOps`: dchg only;
- GI disabled;
- integrity disabled;
- qchg disabled;
- dupd disabled;
- `OptFlds`: reason-for-inclusion + DataSet-name;
- exact RptID/DataSet/member/reason validation;
- report monitor cleanup;
- TrgOps/OptFlds restoration;
- fresh-association cleanup closure.

A separate auxiliary MMS association is strictly read-only. It captures the final pre-command baseline and then samples only the qualified A2.1 command-focus members at high speed.

## Command authority

P1 does not call or wrap `ExecuteControlAsync`.

The operator issues exactly one already-proven safe OPEN/CLOSE through the normal ARSAS control UI after this status appears:

`G2.6-P1 A3 READY — ISSUE ONE ARSAS COMMAND`

The A3 witness consumes the already-existing `Iec61850MonitorRuntime.Diagnostic` entry beginning with:

`Control execution requested:`

That diagnostic is emitted by the existing runtime before native control execution. P1 therefore observes the established control path without inserting a new SBO/SBOw/Operate hook, delaying it, or re-issuing it.

## PASS contract

A3 PASS requires all of the following in the same bounded armed window:

1. core dchg-only activation is proven;
2. the exact existing ARSAS command is captured after the final read-only baseline is ready;
3. at least one qualified command-focus MMS member changes after that command;
4. a valid spontaneous InformationReport is received with reason-for-inclusion `data-change`;
5. the report includes at least one **same exact DataSet index** as the post-command qualified transition;
6. report monitor cleanup succeeds;
7. temporary proof fields are restored;
8. fresh-association cleanup closure succeeds.

The evidence window records the command object/request, transition member/index/before/after values, report included indexes/reasons, correlated indexes, and cleanup state.

## Failure localization

The combined proof separates several useful failure classes:

- report path never arms -> activation/configuration problem;
- command is not captured -> ARSAS stimulus/capture problem;
- command captured but no qualified transition -> wrong/non-changing qualified member or physical/control feedback problem;
- command-bound qualified transition occurs but no dchg report -> report emission/receive-path problem;
- dchg report arrives but includes different indexes -> report/member correlation problem;
- report succeeds but cleanup fails -> production remains ineligible and cleanup must be fixed first.

## Production boundary

A3 success is intentionally weaker than production eligibility.

P1 never calls `MarkProductionEligible`, never saves a promoted qualification profile, and never changes Smart Auto policy. After A3, the persisted field state remains `InformationReportProven` until later shadow verification and the complete G2.6 regression acceptance explicitly advance it.

The ARIEC engine pinned by P1 already contains the P0 production consumer (PR #97), but that consumer remains fail-closed for the current field IED while the profile is below `ProductionEligible`.
