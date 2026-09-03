# G2.6 Smart Dynamic RCB Runtime

## Goal

Smart Dynamic RCB is a normal monitoring acquisition path. It is not a commissioning ceremony.

After an IED has an identity-compatible `InformationReportProven` profile with a successful data-change InformationReport, ARSAS may use the exact already-proven dynamic RCB/member envelope during ordinary monitoring without requiring `ProductionEligible` certification.

`ProductionEligible` remains a separate certification boundary and is never synthesized or persisted by this runtime path.

## Operator workflow

There is no G2.6 commissioning hotkey in the normal runtime workflow.

1. Connect the qualified IED normally.
2. Select the required proven signals.
3. Start Monitor.

ARSAS then performs the acquisition decision automatically.

## Runtime order

The ARIEC planner remains authoritative:

`configured static RCB -> guarded exact proven dynamic RCB -> MMS polling residual/fallback`

Static DataSet-backed reporting keeps normal coverage precedence. For residual points that are inside the exact proven InformationReport envelope, guarded dynamic reporting may use only:

- the exact RCB stored in the successful activation + InformationReport evidence;
- the exact ordered InformationReport-proven member envelope;
- at most one dynamic RCB group.

Anything outside that envelope remains on MMS polling.

## Guarded dynamic authorization

Before dynamic planning ARSAS loads the persisted qualification profile using the current stable IED identity/model fingerprint. ARIEC revalidates:

- current association dynamic-report capability;
- profile schema and identity compatibility;
- state `InformationReportProven` or stronger;
- successful RCB activation evidence;
- successful actual `DataChange` InformationReport evidence;
- exact RCB/DataSet identity consistency;
- exact ordered member consistency with the accepted envelope.

No alternate free RCB may substitute for the proven RCB.

## Fresh execution gate

Planning does not grant indefinite write permission. Immediately before activation ARSAS performs fresh report discovery and fresh RCB availability checks, then runs the same guarded ARIEC planner again with the PlanId-bound qualification context.

If the exact dynamic segment cannot be reproduced, no dynamic write occurs and MMS polling remains active.

## Runtime report + MMS validation

When the dynamic RCB activates successfully, InformationReport traffic drives the live process values. The existing ARSAS runtime continues MMS verification/reconciliation. If MMS detects a process-value change that was not delivered by the armed report, the point is degraded to MMS fallback until report delivery is verified again.

This is intentionally simpler than the physical shadow collector: report quality/timestamp certification is not a prerequisite for guarded runtime operation when the actual proven DataSet carries scalar process values such as `stVal`.

## Failure handling

A real dynamic activation failure opens the existing per-device, process-lifetime dynamic-write circuit breaker. ARSAS does not repeatedly mutate the RCB. Static reporting remains eligible and affected residual points use bounded MMS polling.

Static-to-dynamic recovery also preserves the original PlanId-bound guarded context. Recovery therefore cannot select an arbitrary alternate dynamic RCB; it is still restricted to the exact InformationReport-proven RCB/member envelope and requires proven cleanup if a failed static activation already mutated RCB state.

## State boundary

This runtime path performs no qualification profile save and never calls `MarkProductionEligible`.

The persisted profile may remain:

`InformationReportProven`

while guarded Smart Dynamic RCB is used for normal monitoring.

This means:

`Smart Dynamic runtime authorized != ProductionEligible certification`
