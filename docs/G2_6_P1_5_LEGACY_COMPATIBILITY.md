# G2.6 P1.5 Legacy InformationReportProven Compatibility

## Purpose

P1.5 closes one field-specific compatibility gap in the normal Smart Dynamic RCB runtime path.

The reviewed field profile was legitimately persisted as `InformationReportProven` by the earlier G2.4 transaction, but that stored InformationReport proof is GI-classified. A later G2.5 / deterministic A3 run then independently proved a real **NO-GI spontaneous data-change InformationReport** on the same exact IED identity, exact proven URCB, and exact ordered Q0 CSWI/XCBR member envelope. That later commissioning path was intentionally read-only and therefore did not rewrite the stored profile.

P1.5 lets normal monitoring consume those two pieces of evidence together without editing the qualification JSON and without weakening the guarded runtime planner.

## Exact reviewed compatibility scope

Compatibility is hard-bound to all of the following:

- stable identity: `ied:AA1C1F08R4`
- model fingerprint: `sha256:50c691318c6d6a16b68b121ac48627c26e6e32b937836d559dca1b9eb559f0d9`
- profile revision: `e5f7fe9b93524f8019ff7cd01f042fc1827ef32e8b930262a2eafbf20ef357c0`
- persisted/proven URCB: `AA1C1F08R4ADD/LLN0.RP.A_URCB01`
- exact ordered members:
  1. `AA1C1F08R4Q0/CSWI1$ST$Pos$stVal`
  2. `AA1C1F08R4Q0/XCBR1$ST$Pos$stVal`

The reviewed later A3 evidence also records:

- temporary dchg proof DataSet `AA1C1F08R4ADD/LLN0.AR_G25A_4E20EC7E`;
- actual spontaneous InformationReport received;
- `reason=data-change`;
- included/correlated DataSet indexes `[0,1]`;
- GI disabled;
- exact member mapping;
- association healthy after the report;
- monitor cleanup, TrgOps/OptFlds restore, and fresh-association closure passed.

The later temporary DataSet name is evidence of the later physical dchg transaction, not a replacement for the persisted G2.4 DataSet identity. The compatibility contract binds the two phases through exact IED identity, exact proven RCB and exact ordered member sequence.

## ARIEC authority

ARIEC61850 PR #101 adds `MmsGuardedDynamicReportLegacyCompatibilityPolicy` and typed `MmsDynamicReportLegacyDataChangeCompatibilityEvidence`.

The adapter accepts a legacy GI-classified profile only after checking:

- supported profile schema;
- current profile/IED identity compatibility;
- `InformationReportProven` or stronger state;
- successful persisted RCB activation and actual InformationReport proof;
- exact persisted activation/report RCB identity;
- exact persisted activation/report DataSet identity;
- exact ordered activation/report member equality;
- membership inside the accepted exact envelope;
- complete application-supplied physical dchg evidence;
- exact current stable identity, fingerprint and profile revision;
- exact proven RCB and exact ordered member equality.

If all gates pass, ARIEC creates an **in-memory compatibility view only** in which the already-successful report proof is treated as DataChange for the existing guarded planner. The original profile is not mutated or saved.

The normal guarded planner then still performs fresh association capability, live RCB availability, exact envelope restriction, and at-most-one dynamic RCB group checks immediately before runtime activation.

## ARSAS integration

`DynamicReportGuardedLegacyCompatibilityEvidenceRegistry` contains the reviewed field evidence as an exact manifest, not a wildcard migration rule.

`NativeIec61850Client.HybridReporting.GuardedRuntime` now behaves as follows:

1. load the identity-compatible qualification profile read-only;
2. require a successful activation + actual InformationReport chain;
3. if stored report kind is already `DataChange`, use the normal guarded path unchanged;
4. otherwise require an exact registry match;
5. ask ARIEC to build the compatibility view;
6. preserve that PlanId-bound guarded context through fresh execution revalidation;
7. if any check fails, withhold dynamic mutation and retain static/MMS fallback behavior.

## Safety invariants

P1.5 does **not**:

- edit or delete the persisted qualification profile;
- call `DynamicReportQualificationProfileStore.SaveAsync` from normal runtime;
- call `MarkProductionEligible`;
- convert GI itself into dchg evidence;
- accept a different IED, firmware/model fingerprint or profile revision;
- substitute another free RCB;
- reorder, broaden or guess members;
- bypass fresh live RCB availability;
- remove the process-lifetime dynamic-write circuit breaker;
- remove MMS verification/fallback.

`InformationReportProven guarded compatibility != ProductionEligible certification`.

## Field acceptance after CI

No commissioning hotkey is required for P1.5 validation. Use the normal application path:

1. Open the SCL / connect the already-qualified IED.
2. Start Monitor normally.
3. Verify diagnostics explicitly show P1.5 legacy compatibility accepted.
4. Verify the planner emits the exact proven DynamicURCB path rather than `dynamicURCB=0`.
5. Exercise the already-approved Q0 control sequence and verify event-driven dchg updates plus MMS reconciliation.
6. If dynamic activation or report verification fails, the expected behavior is fail-closed fallback, not repeated dynamic writes.

PR #230 remains draft until this normal-runtime field run is reviewed cleanly. `ProductionEligible` remains OFF.
