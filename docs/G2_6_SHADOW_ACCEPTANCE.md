# G2.6 — ARSAS Shadow Acceptance Boundary

## Current state

The deterministic Q0 A3 commissioning proof is complete, but the field profile remains `InformationReportProven`. Production automatic dynamic reporting is intentionally OFF.

ARSAS now pins ARIEC61850 PR #98 on `main`, which adds a pure typed report-vs-independent-MMS shadow evaluator. The application-side acceptance service is:

`DynamicReportShadowVerificationAcceptanceService`

This service is deliberately downstream of physical evidence collection. It performs no MMS I/O, no RCB/DataSet write, no profile save, and no `MarkProductionEligible` call.

## Required evidence

The physical collector must supply one `MmsDynamicReportShadowVerificationEvidence` bound to the exact persisted InformationReport-proven member sequence.

ARSAS production-shadow options currently require:

- at least 2 accepted report observations;
- exact DataSet index/member identity;
- report-to-poll value parity within 3 seconds;
- polling-observed transition to report-edge correlation within 3 seconds;
- report and polling quality evidence on both sides;
- report and polling device timestamp evidence on both sides;
- device timestamp delta <= 250 ms;
- one deliberate reconnect cycle;
- report subscription recovery after reconnect;
- independent polling-reference recovery after reconnect;
- maximum one dynamic activation attempt per association;
- no missing report edge;
- no duplicate report edge;
- no repeated RCB/DataSet mutation loop.

These values are commissioning acceptance thresholds, not a production runtime retry policy.

## Exact envelope gate

Before evaluating any shadow evidence ARSAS reloads the current identity-bound profile and requires:

1. state exactly `InformationReportProven`;
2. successful stored RCB activation proof;
3. successful stored InformationReport proof;
4. non-empty exact qualified member sequence;
5. shadow evidence member sequence exactly equal to the persisted sequence after only `$`/`.` reference normalization.

No alternate RCB/member sequence is accepted by this gate.

## Acceptance candidate

A successful typed shadow is converted through ARIEC's existing `MmsDynamicReportProductionAcceptance` contract. Smart Control and static-reporting regression decisions remain independent explicit inputs and are not inferred from shadow traffic.

Therefore there are three distinct outcomes:

- **Shadow FAIL** — keep `InformationReportProven`; ProductionEligible OFF.
- **Shadow PASS, control/static regression incomplete** — keep `InformationReportProven`; ProductionEligible OFF.
- **Shadow PASS + complete acceptance candidate** — still keep `InformationReportProven`; a separate reviewed promotion action is required later.

`Shadow PASS != ProductionEligible` is an invariant.

## Next implementation step: physical collector

The collector must be built on already-proven ARIEC/ARSAS paths rather than introducing an ad-hoc RCB/control implementation.

Preferred structure:

1. load exact `InformationReportProven` target;
2. reuse the established one-URCB transactional dchg commissioning setup/cleanup for the exact persisted RCB/member envelope;
3. receive exact mapped report observations;
4. use a second isolated read-only MMS association to poll the same exact members;
5. capture real quality and device timestamp evidence only when both sides supply trustworthy values — never synthesize missing q/t;
6. perform one deliberate report/reference reconnect cycle;
7. prove report re-arm + reference re-open;
8. bound dynamic activation to one attempt per association;
9. always execute monitor/RCB/DataSet/proof-field cleanup;
10. pass the resulting typed evidence to `DynamicReportShadowVerificationAcceptanceService`;
11. do not persist or promote profile state automatically.

No automatic OPEN/CLOSE/toggle stimulus should be added to the shadow collector merely to create traffic. Normal process changes or separately reviewed commissioning stimulus remain outside this acceptance service.
