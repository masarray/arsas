# G2.6-P1 — Deterministic Q0 Target-Locked Auto A3 dchg Proof

## Goal

Close the field A3 proof with one exact, already-proven ARSAS control path and remove both sources of physical-test ambiguity discovered during P1:

1. generic command-focus recovery selected the first eight alphabetically ordered control-status members and excluded the intended Q0 control;
2. manual READY → operator click timing could expire without any command being captured.

The field-bounded P1 chain is now:

`AA1C1F08R4Q0/CSWI1.Pos one-shot OPEN -> qualified Q0 MMS status transition -> Dynamic URCB InformationReport(reason=data-change) on the same DataSet index -> cleanup`

This is a commissioning proof only. It does **not** mark the IED `ProductionEligible` and it does **not** enable production automatic dynamic reporting.

## Entry point

Select the qualified field IED in ARSAS, then press:

`Ctrl + Shift + A`

For this P1 field build, the hotkey itself is the explicit commissioning action. There is no successful-path arm dialog, recovery dialog, or manual command dialog. The older A2.1 read-only/manual command witness remains separately available on `Ctrl + Shift + F`.

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

The coordinator never converts the current state into a toggle. `Open`, intermediate, unknown, wrong identity, wrong status mapping, busy control, or a non-operational control model all block command dispatch. There is no automatic CLOSE, retry, opposite command, or restore command.

## Preflight gates

Before the report path is allowed to mutate an RCB, P1 requires:

1. the connected model resolves to the exact field identity and model fingerprint above;
2. the exact Q0 control object exists and exposes the exact `ControlStatusReference` above;
3. the existing ARSAS control inspector reports the exact target operationally ready;
4. the exact target state is `Closed` before recovery/report arming;
5. the persisted profile is identity-compatible and exactly `InformationReportProven`;
6. the G2.4 RCB activation proof and InformationReport proof are successful;
7. the exact G2.4 member sequence still resolves on the live IED;
8. the Q0 A2.1 status/focus chain intersects the exact G2.4-proven DataSet member sequence;
9. no control command is already busy.

The control state is checked again after any recovery and once more after the A3 final witness baseline is ready. The one-shot OPEN is dispatched only if that final READY-time inspection still says exactly `Closed`.

## Field-discovered Q0 command-focus recovery

Physical P1 testing proved that the IED could already be `InformationReportProven` while the exact proven member envelope contained command statuses for DSQZ/ESQZ objects but not the intended `AA1C1F08R4Q0/CSWI1.Pos.stVal`. The previous generic recovery sorted all ARSAS commands by object reference and the eight-member cap was exhausted before Q0 was reached.

P1 now reuses the transactional recovery with a **private target-scoped clone of the discovered signal model**:

1. the normal live `SignalDefinition` instances are never modified;
2. every signal is privately shallow-cloned;
3. only on those private clones, non-Q0 `ControlStatusReference` values are suppressed;
4. identity-significant fields are unchanged, and P1 explicitly recomputes the identity/fingerprint and requires it to be exactly equal to the original model;
5. recovery therefore sees exactly one command focus: `AA1C1F08R4Q0/CSWI1.Pos`;
6. the existing A2.1 focus-chain logic adds the exact Q0 status plus corroborating CSWI/XCBR status candidates when the live IED exposes them;
7. dynamic NamedVariableList qualification runs in the private staging profile store;
8. existing G2.4 V2 proves one-URCB activation + an actual InformationReport against staging;
9. G2.4-C proves fresh-association RCB/DataSet cleanup closure;
10. optimistic concurrency still prevents overwriting newer live evidence;
11. only after every stage closes may the live profile be atomically replaced `InformationReportProven -> InformationReportProven`.

Any failure before final replacement leaves the previous live profile authoritative. Recovery itself issues **zero control commands** and cannot mark `ProductionEligible`.

## Armed transaction

The existing `DynamicReportSpontaneousDataChangeCommissioningService` and `DynamicReportCommandBoundDataChangeCommissioningService` remain authoritative for the report/witness proof:

- one exact InformationReport-proven URCB;
- one bounded temporary dynamic DataSet;
- `TrgOps`: dchg only;
- GI disabled;
- integrity disabled;
- qchg disabled;
- dupd disabled;
- `OptFlds`: reason-for-inclusion + DataSet-name;
- exact RptID/DataSet/member/reason validation;
- a separate read-only MMS witness association;
- final pre-command exact member baseline;
- exact command-bound high-speed transition sampling;
- exact DataSet-index correlation;
- report monitor cleanup;
- TrgOps/OptFlds restoration;
- fresh-association cleanup closure.

The core A3 service still does not execute a control command. It remains an observer of the established runtime diagnostic and the physical status/report evidence.

## One-shot auto stimulus

The new `DynamicReportQ0TargetLockedAutoA3CommissioningService` removes only the operator timing race.

When the core A3 reports its final READY marker after the report path is armed and the read-only final baseline is captured, the coordinator immediately performs one final control inspection. If and only if the target is still exactly operationally ready and `Closed`, it constructs one normal `Iec61850ControlCommandRequest` and calls the already-existing:

`Iec61850MonitorRuntime.ExecuteControlAsync(...)`

No new MMS control implementation is introduced. The normal runtime remains responsible for the existing SBO/SBOw/Operate/CommandTermination sequence and wire evidence. Its existing diagnostic:

`Control execution requested:`

is emitted before native control execution and is therefore consumed by the already-armed A3 witness exactly as before.

The dispatch policy is deliberately one-shot:

- maximum automatic dispatch count: 1;
- requested value: `Open`;
- retry: false;
- automatic CLOSE: false;
- automatic opposite command: false;
- automatic restore: false.

If the READY-time state inspection fails, the A3 coordinator cancels the wait fail-closed rather than waiting for or synthesizing a command. If an already-dispatched physical command later returns ambiguous/error evidence, P1 does not retry it; runtime wire evidence plus physical transition/report evidence remain authoritative.

## PASS contract

A3 PASS still requires all of the following in the same bounded armed window:

1. core dchg-only activation is proven;
2. the exact runtime request for `AA1C1F08R4Q0/CSWI1.Pos -> Open` is captured after the final read-only baseline is ready;
3. at least one qualified Q0 command-focus MMS member changes after that command;
4. a valid spontaneous InformationReport is received with reason-for-inclusion `data-change`;
5. the report includes at least one **same exact DataSet index** as the post-command qualified transition;
6. report monitor cleanup succeeds;
7. temporary proof fields are restored;
8. fresh-association cleanup closure succeeds.

The evidence window remains authoritative for command object/request, transition member/index/before/after values, report included indexes/reasons, correlated indexes, and cleanup state.

## Failure localization

The combined proof now separates these useful failure classes:

- exact identity/fingerprint mismatch -> auto control impossible, zero commands;
- Q0 object/status mismatch -> auto control impossible, zero commands;
- Q0 not `Closed` / not operationally ready -> auto control impossible, zero commands;
- target-scoped recovery cannot preserve model fingerprint -> recovery/control blocked;
- recovery DataSet qualification fails -> Q0 NamedVariableList capability problem; old profile remains untouched;
- staged G2.4 fails -> RCB activation or actual InformationReport problem; old profile remains untouched;
- staged G2.4-C fails -> fresh cleanup closure problem; old profile remains untouched;
- concurrency gate fails -> newer profile evidence exists; recovery refuses overwrite;
- report path never arms -> zero auto commands;
- READY-time reinspection fails -> zero auto commands and no retry;
- exact Q0 command captured but no qualified transition -> physical/control feedback problem;
- Q0 transition occurs but no dchg report -> report emission/receive-path problem;
- dchg report arrives but includes different indexes -> report/member correlation problem;
- report succeeds but cleanup fails -> production remains ineligible.

## Production boundary

A3 success is intentionally weaker than production eligibility.

The recovery path may atomically replace one `InformationReportProven` profile with another `InformationReportProven` profile after stronger Q0-focused staging evidence, but neither recovery nor Auto A3 can advance to `ProductionEligible`. The core A3 remains read-only with respect to persisted profile state. Smart Auto production authorization therefore stays fail-closed until later shadow verification and the complete G2.6 regression acceptance explicitly advance the profile.

The ARIEC engine pinned by P1 already contains the P0 production consumer (PR #97), but that consumer remains fail-closed for the current field IED while the profile is below `ProductionEligible`.
