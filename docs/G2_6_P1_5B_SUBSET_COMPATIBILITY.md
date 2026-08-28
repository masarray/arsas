# G2.6 P1.5b — Subset-Scoped Legacy Compatibility

## Purpose

P1.5b closes the normal-runtime Smart Dynamic RCB compatibility gap exposed by the physical field run on `AA1C1F08R4`.

The persisted `InformationReportProven` chain is valid but broader than the later deterministic A3 data-change proof. The persisted chain contains six ordered members and remains unchanged qualification evidence. The later A3 run physically proved only the first two members as a spontaneous NO-GI `data-change` report on the same exact URCB.

P1.5b does not rewrite the legacy GI-classified proof into a broader DataChange proof. It authorizes guarded dynamic runtime only for the exact later-proven two-member ordered subset.

## Exact field scope

Identity:

- stable identity: `ied:AA1C1F08R4`
- model fingerprint: `sha256:50c691318c6d6a16b68b121ac48627c26e6e32b937836d559dca1b9eb559f0d9`
- profile revision: `e5f7fe9b93524f8019ff7cd01f042fc1827ef32e8b930262a2eafbf20ef357c0`
- exact proven URCB: `AA1C1F08R4ADD/LLN0.RP.A_URCB01`

Persisted six-member qualification chain, in order:

1. `AA1C1F08R4Q0/CSWI1$ST$Pos$stVal`
2. `AA1C1F08R4Q0/XCBR1$ST$Pos$stVal`
3. `AA1C1F08R4Q0/CSWI1$ST$Beh$stVal`
4. `AA1C1F08R4Q0/CSWI1$ST$Health$stVal`
5. `AA1C1F08R4Q0/CSWI1$ST$Loc$stVal`
6. `AA1C1F08R4Q0/CSWI1$ST$LocKey$stVal`

Later physical NO-GI dchg subset, in order:

1. `AA1C1F08R4Q0/CSWI1$ST$Pos$stVal`
2. `AA1C1F08R4Q0/XCBR1$ST$Pos$stVal`

The A3 field evidence used temporary DataSet `AA1C1F08R4ADD/LLN0.AR_G25A_4E20EC7E`, received an actual spontaneous InformationReport with `reason=data-change`, correlated DataSet indexes `[0,1]`, kept GI disabled, retained a healthy association, and completed monitor/proof-field/fresh-association cleanup.

The temporary A3 DataSet name is transaction evidence; it does not replace or edit the persisted G2.4 DataSet identity. The two phases are joined only through the exact current identity, exact RCB, and exact ordered dchg member subset.

## ARIEC authority

ARIEC61850 PR #102, merged on `main` at `0965f67fe912355b3b29fc8123872a68d4064b04`, adds:

- `MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy`
- `MmsGuardedDynamicReportLegacySubsetRuntimePlanner`

The P1.5b validator requires all of the following before the subset may be considered:

- supported profile schema;
- current profile/IED identity compatibility;
- `InformationReportProven` or stronger persisted state;
- successful persisted activation and actual InformationReport chain;
- legacy stored report kind exactly `GeneralInterrogation`;
- exact persisted activation/report RCB equality;
- exact persisted activation/report DataSet equality;
- exact full persisted activation/report member-sequence equality;
- full persisted report sequence remains inside the accepted qualification envelope;
- complete separate physical NO-GI dchg evidence;
- exact current stable identity, fingerprint and profile revision;
- exact physical-evidence RCB equals the persisted proven RCB;
- physical dchg members are nonempty and unique;
- physical dchg members are an ordered subset of both the persisted report sequence and accepted envelope.

The runtime planner then limits automatic dynamic planning to that physical dchg subset, one exact dynamic RCB group maximum. It never treats the remaining four legacy members as dchg-proven.

## ARSAS integration

`DynamicReportGuardedLegacyCompatibilityEvidenceRegistry` now models two independent scopes:

- the exact six-member persisted qualification sequence;
- the exact two-member later A3 dchg sequence.

`NativeIec61850Client.HybridReporting.GuardedRuntime` dispatches normal monitoring as follows:

1. no trusted guarded context -> normal capability-aware static/polling planner;
2. persisted report kind already `DataChange` -> existing guarded runtime planner;
3. reviewed GI-classified legacy profile + exact P1.5b manifest -> P1.5b subset runtime planner;
4. any mismatch -> fail closed; no legacy compatibility authorization.

The original profile context is retained unchanged. There is no in-memory conversion of the six-member GI proof into a DataChange proof.

The same PlanId-bound guarded context is used at fresh execution revalidation. The exact P1.5b registry is resolved again before the subset planner is used, so planning does not become indefinite write permission.

## Safety invariants

P1.5b does not:

- edit, replace or delete persisted qualification JSON;
- call `DynamicReportQualificationProfileStore.SaveAsync` from normal runtime;
- call `MarkProductionEligible`;
- claim the full six-member sequence is dchg-proven;
- reorder or broaden the two-member physical dchg scope;
- substitute another free RCB;
- bypass fresh association capability or RCB availability checks;
- remove the process-lifetime dynamic-write circuit breaker;
- remove MMS verification/fallback;
- change strict shadow q/t certification acceptance.

`P1.5b guarded runtime != ProductionEligible certification`.

## Normal-runtime field acceptance

No qualification or shadow hotkey is required.

1. Open SCL.
2. Connect the already-qualified IED.
3. Start Monitor normally.
4. Confirm diagnostics say P1.5b subset compatibility was accepted.
5. Confirm the acquisition plan emits a DynamicURCB path for the exact Q0 CSWI/XCBR subset rather than `dynamicURCB=0`.
6. Confirm the exact RCB is `AA1C1F08R4ADD/LLN0.RP.A_URCB01`.
7. Confirm dynamic DataSet definition/binding and `RptEna=true` succeed.
8. Exercise one approved Q0 state change and confirm a spontaneous `data-change` InformationReport updates the two subset points.
9. Confirm MMS reconciliation remains healthy.
10. Disconnect/reconnect and verify fresh revalidation/re-arm without a repeated dynamic mutation loop.

If any dynamic activation/report gate fails, expected behavior is fail-closed static/MMS fallback. PR #230 remains draft and unmerged until the normal-runtime field evidence is reviewed cleanly.
