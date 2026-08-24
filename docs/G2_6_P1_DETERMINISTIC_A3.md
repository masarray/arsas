# G2.6-P1 — Deterministic Q0 Target-Locked Auto A3 dchg Proof

## Goal

Close the field A3 proof with one exact, already-proven ARSAS control path and remove the sources of physical-test ambiguity discovered during P1.

The field-bounded P1 chain is:

`AA1C1F08R4Q0/CSWI1.Pos one-shot OPEN -> accepted native MMS control -> qualified Q0 MMS status transition -> post-command Dynamic URCB InformationReport(reason=data-change) on the same exact DataSet index -> cleanup`

This is a commissioning proof only. It does **not** mark the IED `ProductionEligible` and it does **not** enable production automatic dynamic reporting.

## Entry point

Select the qualified field IED in ARSAS, then press `Ctrl + Shift + A`.

For this P1 field build, the hotkey itself is the explicit commissioning action. The older A2.1 read-only/manual command witness remains separately available on `Ctrl + Shift + F`.

## Exact field lock

Auto A3 is deliberately bounded to all of the following exact values:

- stable identity: `ied:AA1C1F08R4`;
- model fingerprint: `sha256:50c691318c6d6a16b68b121ac48627c26e6e32b937836d559dca1b9eb559f0d9`;
- control object: `AA1C1F08R4Q0/CSWI1.Pos`;
- control status: `AA1C1F08R4Q0/CSWI1.Pos.stVal`;
- stimulus: `Open` only;
- interlock check: enabled;
- synchrocheck: disabled;
- test mode: disabled.

The coordinator never converts current state into a toggle. `Open`, intermediate, unknown, wrong identity, wrong status mapping, busy control, or a non-operational control model all block dispatch. There is no automatic CLOSE, retry, opposite command, or restore command.

## Preflight and target-scoped recovery

Before the report path is allowed to mutate an RCB, P1 requires exact identity/fingerprint, exact Q0 control/status mapping, operational readiness, exact `Closed` state, identity-compatible `InformationReportProven`, successful persisted G2.4 activation/report proof, live resolution of the persisted member sequence, Q0 command-focus intersection, and no control command already busy.

If Q0 is missing from the persisted G2.4 member envelope, P1 reuses transactional recovery on a **private target-scoped clone** of the discovered signal model. Live `SignalDefinition` instances are not modified. Non-Q0 command focus is suppressed only on private clones, identity-significant fields remain unchanged and are revalidated, qualification runs in a staging profile store, G2.4 V2 proves activation + actual InformationReport, G2.4-C proves fresh cleanup, optimistic concurrency prevents overwriting newer evidence, and the live profile is replaced only after all staging gates pass. Recovery itself issues zero control commands and cannot mark `ProductionEligible`.

## Armed transaction and one-shot control

The existing `DynamicReportSpontaneousDataChangeCommissioningService` and `DynamicReportCommandBoundDataChangeCommissioningService` remain authoritative for the dchg-only report/witness proof:

- one exact InformationReport-proven URCB;
- one bounded temporary dynamic DataSet;
- `TrgOps`: dchg only;
- GI/integrity/qchg/dupd disabled;
- `OptFlds`: reason-for-inclusion + DataSet-name;
- exact RptID/DataSet/member/reason validation;
- separate read-only MMS witness association;
- final pre-command exact member baseline;
- exact command-bound high-speed transition sampling;
- exact DataSet-index correlation;
- report monitor cleanup;
- TrgOps/OptFlds restoration;
- fresh-association cleanup closure.

The core A3 service still does not execute a control command. The Q0 coordinator removes only the operator timing race. At READY it performs one final control inspection and, only if Q0 is still exactly operationally ready and `Closed`, calls the already-existing `Iec61850MonitorRuntime.ExecuteControlAsync(...)` once with `Open`. No new MMS control implementation is introduced. There is no retry, CLOSE, toggle/opposite command, or automatic restore.

## Physical field acceptance — PASS, 2026-08-24

The physical acceptance run was executed on implementation head `4eedc1449b15ddc24f048040805cef4e508a6dd9` and produced the following operator-captured evidence:

- exact command: `AA1C1F08R4Q0/CSWI1.Pos -> Open`;
- command intent observed at `2026-08-24T10:21:37.0572634+00:00`;
- `AA1C1F08R4Q0/XCBR1.Pos.stVal` transitioned `bits(80) -> bits(40)` about `470.638 ms` after command;
- `AA1C1F08R4Q0/CSWI1.Pos.stVal` transitioned `bits(80) -> bits(40)` about `491.29 ms` after command;
- spontaneous InformationReport was proven with `reason=data-change`;
- report included exact DataSet indexes `[0,1]`;
- command-bound changed indexes were `[0,1]`;
- correlated indexes were `[0,1]`;
- report monitor cleanup passed;
- TrgOps/OptFlds restoration passed;
- fresh-association cleanup closure passed;
- report association remained healthy.

That run closed the original P1 physical command -> qualified transition -> dchg InformationReport -> same exact DataSet index -> cleanup contract.

## Final fail-closed correlation hardening before merge

Before merge, two additional false-positive paths were closed on merge-candidate head `d9a8f83b06b025b954c486ad467581df2347a387`.

### Native control acceptance is mandatory

`Control execution requested:` is command intent only. It cannot independently satisfy PASS because it is emitted before native control execution completes.

P1 now also requires a later successful native-control diagnostic from the **same existing runtime control path**, for the same exact object and requested value, with MMS response/wire evidence. `NotSent`, rejected, no-response, and otherwise unproven native control fail closed. No second SBO/SBOw/Operate implementation was introduced.

### Report reception must follow the command

The selected valid dchg frame preserves `MmsReportFrame.ReceivedAt` as `ReportReceivedAtUtc`. P1 now requires:

`ReportReceivedAtUtc > CommandObservedAtUtc`

before command/report correlation may pass. A valid unrelated dchg frame received before the command can no longer be combined with a later same-index MMS transition to create a false PASS.

These changes only make acceptance stricter; they do not broaden command authority or production eligibility. The original physical run predates these extra software gates, so it is recorded as physical acceptance of the original P1 contract, not falsely described as a physical rerun of the final hardening head.

## Final PASS contract

A final-head A3 PASS requires all of the following in the same bounded armed window:

1. exact identity-compatible `InformationReportProven` profile;
2. exact Q0 command-focus intersection with the persisted G2.4 member sequence;
3. exact dchg-only URCB activation with no GI;
4. exact ARSAS Q0 command intent after the final read-only baseline;
5. successful native MMS control result/wire evidence for that exact request;
6. post-command transition on a qualified command-focus member;
7. valid spontaneous `reason=data-change` InformationReport;
8. selected report receive time strictly after the captured command time;
9. at least one same exact DataSet index between command-bound transition and report;
10. report monitor cleanup PASS;
11. TrgOps/OptFlds restore PASS;
12. fresh-association cleanup closure PASS.

## Final CI validation

Merge-candidate head: `d9a8f83b06b025b954c486ad467581df2347a387`.

- Build ARSAS #1422: PASS;
- full solution build: PASS, 0 errors;
- ARSAS regression suite: **583/583 PASS**, 0 failed, 0 skipped;
- portable single EXE publish + smoke test: PASS;
- Windows installer #372: PASS;
- IO List validation #365: PASS;
- SV evidence validation #534: PASS;
- immutable ARIEC61850 engine: `main` @ `aa2ddfb47af5f3b806858553568792fbc21a64f1`.

## Production boundary and next phase

P1 success remains intentionally weaker than production eligibility. The persisted state remains `InformationReportProven`; `ProductionEligible` and production automatic dynamic reporting remain OFF.

P1 is stacked onto `g2.6-smart-dynamic-rcb`, so its merge target is that Smart Dynamic branch rather than `main`. After P1 merge, the next engineering gate is Smart Dynamic shadow verification: dynamic reporting operates under controlled observation while MMS polling remains the reconciliation/reference path. Only later shadow/regression acceptance may authorize a production eligibility transition.