# IEDScout Convergence Contract

## Product target

ARSAS targets IEDScout-equivalent IEC 61850 engineering semantics with lower wire cost where possible:

1. one accepted MMS association and bounded structure-first discovery;
2. a complete canonical model with exact LN/DO/SDO/DA/FC identity;
3. Edition 2 IID / Edition 1 ICD that can be reopened by ARSAS, reconnect to the same relay, hydrate values without full discovery, and run configured static reporting.

The machine-readable authority is `evidence/iedscout-convergence-target.json`.

## Merged proven baseline

The physical R10 baseline was tested with:

- ARSAS `eb8eb13d491f9aa265205852b8a4bab07af440ff`;
- ARIEC61850 tested head `9935d6902d786cc69b299260fe36b835944d5e81`;
- ARIEC61850 merged-main commit `648124097621046f5f127ceb1cf853fea54db730`.

The tested engine head and merged-main commit have the identical tree SHA
`1cf7e08f333f24994625e8fe8416dbd0a16195b1`.

Merged engine provenance:

- PR #134 → main merge `e6779ff74e5716af4fcfc3dc926dae0567b3cdb0`: Smart Discovery performance authority.
- PR #135 → main merge `648124097621046f5f127ceb1cf853fea54db730`: canonical model, SCL interoperability, P1/P2 value pipeline.
- ARSAS PR #324: consumer integration and physical R10 proof.

## P0 — structural discovery freeze

Contract: `P0-R9-STRUCTURAL`.

AA1E1F06R4 physical reference is locked at one association, 323 confirmed MMS
requests, 138 GetNameList, 119 GetVariableAccessAttributes, 2
GetNamedVariableListAttributes and 64 Reads, while preserving 32 LD / 119 LN /
860 top-level DO / 906 DO+SDO / 4925 scalar leaves / 2 DataSets / 58 FCDA / 32
logical ReportControls / 1 SettingControl.

The same-relay IEDScout comparison remains about 417 confirmed requests, 119 GVA
and 156 Reads. ARSAS must not add traffic merely to imitate IEDScout.

Forbidden regressions include a second discovery association, legacy supplemental
browse, a second full GetNameList sweep, recursive per-leaf GVA, and eager FC-root
value hydration on the discovery critical path.

## P1 — CF projection-order repair: physically proven

Contract: `P1-CF-DO-SCOPED`.

R9 exposed 46 projection errors because SCL LNodeType DO order was incorrectly used
as MMS CF-root child order. P1 reads multi-DO CF data through exact `LN$CF$DO`
references and batches those structured reads.

R10 physical reuse closes P1:

- Ed2: 709 initial targets, 525 FC-root targets, 184 DO-scoped targets,
  709/709 successful, 0 failed, `projectionErrors=0`.
- Ed1: 708 initial targets, 524 FC-root targets, 184 DO-scoped targets,
  708/708 successful, 0 failed, `projectionErrors=0`.

No extra discovery GVA or second association was introduced.

## P2 — exact-case value pipeline: physically proven

Contract: `P2-CASE-EXACT-VALUES`.

R9 Ed2 lost 11 projected values because a consumer cache used case-insensitive
identity. P2 uses exact-case identity through TypeSpecification mapping, initial
projection, trusted-SCL caching, canonical evidence and SCL instance-value targeting.

R10 physical reuse closes P2:

- Ed2: `projectedUniqueValues=4441`, `initialValueCache=4441`, `cacheLoss=0`.
- Ed1: `projectedUniqueValues=4246`, `initialValueCache=4246`, `cacheLoss=0`.

## R10 round-trip/reporting acceptance

Both generated editions reopen as trusted SCL and reconnect without full discovery:

- expected/observed/matched MMS domains: 32/32/32;
- DataSets: 2, members: 58, missing members: 0;
- configured report plans: Digital BRCB 36 members + Analog URCB 22 members;
- report-covered runtime points: 58;
- final unresolved runtime points: 0;
- cyclic MMS process polling: 0;
- actual InformationReport traffic observed on both editions.

The early UI field `Primary unresolved=2` is not an operational loss: the exact
static DataSet schema resolves all 58 runtime points before reporting starts.

## Save-time instance values

R10 physically proves bounded Save SCL enrichment while fast discovery stays
unchanged:

- canonical instance evidence: 3854 leaves;
- Ed2 exported `Val`: 3202;
- Ed1 exported `Val`: 3106.

IEDScout's golden file has about 1529 `Val` elements. A larger count is not
automatically better; the remaining task is semantic path/value comparison, not
count chasing.

## RCB lock

The accepted representation remains:

- 34 runtime RCB objects → 32 logical SCL ReportControls;
- `Services/ConfReportControl max=34`;
- Buffer/Digital and Unbuffer/Analog remain the two configured report authorities;
- preallocated ADD slots without DataSet never receive an invented `datSet`.

## Next improvements — do not disturb the proven wire/reuse path

### 1. Template interning

R10 Ed2 is semantically correct but verbose:

- ARSAS: 119 LNodeType / 906 DOType / 752 DAType / about 731 KB;
- IEDScout reference: about 38 / 60 / 17 / about 247 KB.

Next work may intern only templates with identical ordered semantic fingerprints.
Expanded model counts, FC ownership, values, DataSets/RCBs and round-trip behavior
must remain unchanged.

### 2. Reuse one save-enrichment snapshot across Ed2 and Ed1

When Ed2 and Ed1 are saved in the same unchanged live association/model generation,
both currently derive the same 3854 instance-evidence leaves. A later optimization
may reuse that bounded snapshot across serializers, but never across reconnect,
model-generation change or explicit refresh.

### 3. Semantic Val diff

Compare ARSAS vs IEDScout by exact
`LD/LN/DO/SDO/DA/BDA/FC/bType/value` path, not raw XML position and not total
`Val` count.

## Regression signatures that are forbidden

A build is rejected if it restores any of these patterns:

- legacy `DiscoverAsync` as the public discovery route;
- a second supplemental discovery association;
- recursive per-leaf GVA expansion;
- speculative thousands of Reads on the discovery path;
- cross-DO positional CF projection;
- case-insensitive IEC 61850 member/value identity;
- WYE/DEL/SEQ SDO flattening;
- Edition 2 tracking CDCs in Edition 1;
- invented DataSet bindings for unassigned RCB slots;
- silent canonical-save success without reopen/association validation.

## Promotion state

The R10 physical retest has passed and merge is allowed. Production promotion is
still a separate release decision.

The field result, not test count alone, decides interoperability acceptance.
