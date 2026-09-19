# IEC 61850 Interoperability Convergence Contract

## Current stable baseline

ARSAS **1.6.38** is the current verified stable Windows release.

Release source:

- ARSAS release commit: `5e7ebec2779177c7f182a6a30b64218f84200a90`
- R10 post-merge baseline: `b93ef8fb615213eb81cdfda66d89c05a0d658839`
- ARIEC61850 merged authority: `648124097621046f5f127ceb1cf853fea54db730`
- ARIEC61850 physical-tested head: `9935d6902d786cc69b299260fe36b835944d5e81`
- Engine source-tree SHA: `1cf7e08f333f24994625e8fe8416dbd0a16195b1`

The tested engine head and merged engine authority resolve to the same source tree.

The machine-readable authority is
`evidence/interoperability-convergence-target.json`.

## Acceptance model

The accepted path keeps discovery, SCL generation, trusted-SCL reuse and reporting
as separate bounded phases:

1. one accepted MMS association for structural discovery;
2. complete LD/LN/DO/SDO/DA/FC identity;
3. canonical Edition 2 IID or Edition 1 ICD generation;
4. reopen through the ARSAS SCL workspace;
5. reconnect using the trusted SCL model;
6. bounded initial value hydration;
7. static DataSet/report activation;
8. runtime value acquisition through report traffic without cyclic MMS process polling.

## P0 — structural discovery freeze

Contract: `P0-R9-STRUCTURAL`.

The physical acceptance device is locked at:

- 1 MMS association;
- 323 confirmed MMS requests on the accepted R10 path;
- 138 GetNameList requests;
- 119 GetVariableAccessAttributes requests;
- 2 GetNamedVariableListAttributes requests;
- 64 Reads;
- 32 logical devices;
- 119 logical nodes;
- 860 top-level data objects;
- 906 data objects including SDOs;
- 4925 scalar leaves;
- 2 DataSets / 58 FCDA;
- 32 logical ReportControls;
- 1 SettingControl.

Regression ceilings remain bounded at 417 confirmed MMS requests,
119 GetVariableAccessAttributes requests and 156 Reads.

Forbidden regressions include:

- a second discovery association;
- supplemental legacy browsing;
- a second full GetNameList sweep;
- recursive per-leaf GVA expansion;
- eager FC-root value hydration on the structural discovery critical path.

## P1 — exact DO-scoped CF projection

Contract: `P1-CF-DO-SCOPED`.

R9 exposed 46 projection errors caused by using SCL LNodeType DO order as if it
were MMS multi-DO CF child order. The accepted repair reads multi-DO CF data
through exact `LN$CF$DO` references and keeps batching bounded.

R10 physical reuse proves the repair:

### Edition 2

- initial targets: 709;
- FC-root targets: 525;
- DO-scoped targets: 184;
- successful Reads: 709/709;
- failed Reads: 0;
- `projectionErrors=0`.

### Edition 1

- initial targets: 708;
- FC-root targets: 524;
- DO-scoped targets: 184;
- successful Reads: 708/708;
- failed Reads: 0;
- `projectionErrors=0`.

## P2 — exact-case value identity

Contract: `P2-CASE-EXACT-VALUES`.

The accepted value pipeline preserves case-distinct IEC 61850 paths through
TypeSpecification mapping, trusted-SCL projection, cache storage, canonical
instance evidence and SCL instance-value targeting.

R10 proves zero cache loss:

- Edition 2: `projectedUniqueValues=4441`,
  `initialValueCache=4441`, `cacheLoss=0`;
- Edition 1: `projectedUniqueValues=4246`,
  `initialValueCache=4246`, `cacheLoss=0`.

## R10 trusted-SCL round trip

Both generated editions reopen and reconnect through the trusted-SCL path.

Common acceptance:

- expected/observed/matched MMS domains: 32/32/32;
- static DataSets: 2;
- static DataSet members: 58;
- missing static members: 0;
- report-backed runtime points: 58/58;
- final unresolved runtime points: 0;
- cyclic MMS process polling: 0;
- actual InformationReport traffic observed.

The intermediate field `Primary unresolved=2` is not an operational loss.
Static DataSet identity resolves all 58 runtime points before report-backed
acquisition is accepted.

## Save-time instance evidence

Save-time value enrichment remains outside the structural discovery critical
path.

R10 physical results:

- canonical instance evidence: 3854 leaves;
- Edition 2 exported `Val`: 3202;
- Edition 1 exported `Val`: 3106.

Raw `Val` count is not treated as a quality metric. Exact semantic
path/value correctness remains the acceptance criterion.

## ReportControl identity

The accepted representation remains:

- 34 concrete runtime RCB objects;
- 32 logical SCL ReportControls;
- `Services/ConfReportControl max=34`;
- 36 digital + 22 analog static DataSet members;
- unassigned indexed slots remain unassigned and do not receive invented
  DataSet references.

## Release verification

ARSAS 1.6.38 passed:

- exact release-source restore/build/test;
- Windows portable publish and smoke test;
- Windows installer build and silent install/uninstall smoke test;
- R10 post-merge convergence verification;
- SHA-256 generation;
- SPDX 2.3 SBOM generation;
- app/engine provenance generation;
- artifact attestation;
- public GitHub Release publication.

Public release identity and package hashes are synchronized in
`landing/latest.json`.

## Next bounded improvements

### Template interning

Edition 2 is semantically correct but can reduce duplicate type templates.
Interning is allowed only when ordered semantic fingerprints are identical.

The expanded model counts, FC ownership, values, DataSets, ReportControls and
round-trip behavior must remain unchanged.

### Save-enrichment snapshot reuse

A later optimization may reuse one bounded save-enrichment snapshot across
Edition 2 and Edition 1 serialization only while the association and model
generation are unchanged.

The snapshot must be invalidated on reconnect, model-generation change or
explicit refresh.

This optimization is not part of ARSAS 1.6.38.

## Regression signatures that remain forbidden

A build is rejected if it restores any of these patterns:

- legacy discovery as the primary route;
- a second supplemental discovery association;
- recursive per-leaf GVA expansion;
- speculative high-volume Reads on the structural discovery path;
- cross-DO positional CF projection;
- case-insensitive IEC 61850 member/value identity;
- WYE/DEL/SEQ SDO flattening;
- invalid Edition 1 tracking CDC output;
- invented DataSet bindings for unassigned RCB slots;
- silent canonical-save success without reopen/association validation.

The physical result, not test count alone, decides interoperability acceptance.
