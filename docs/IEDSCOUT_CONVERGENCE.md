# IEDScout Convergence Contract

## Product target

ARSAS has one active IEC 61850 convergence target:

1. **Discovery parity:** one accepted MMS association, structure-first bounded discovery, no supplemental legacy browse, no recursive per-leaf GVA storm, and physical performance comparable to IEDScout on the same relay.
2. **Canonical model correctness:** exact Logical Node identity, correct DO/SDO hierarchy, Functional Constraint ownership from evidence, complete standard semantic authority where proven, and no model deletion used as a safety mechanism.
3. **Saved SCL usability:** full-model IID/ICD must reopen in ARSAS, rebuild the exact accepted association plan, reconnect to the physical relay, and remain usable for bounded initial reads and static reporting.

The machine-readable authority is `evidence/iedscout-convergence-target.json`.

Current R8 model-repair engine authority: `45eab0fbc765a6aa3a1c7a3b72a0293b97eb3fb0`.

## Active stacked PRs

Only this stack is active for this target:

- ARIEC61850 PR #134 — discovery/performance authority.
- ARIEC61850 PR #135 — canonical model, association evidence, semantic SCL and schema authority, stacked on #134.
- ARSAS PR #324 — exact consumer integration and physical-evidence build.

No new discovery/SCL work should branch from older trial PRs. Old PRs remain provenance only unless explicitly revalidated and restacked onto the active authority.

## Regression signatures that are forbidden

A build is rejected if it restores any of these patterns:

- public discovery routes to legacy `DiscoverAsync` instead of the smart field-test route;
- a second supplemental MMS association is opened for discovery;
- recursive per-leaf GetVariableAccessAttributes expansion replaces structure-first probing;
- thousands of speculative sibling/engineering-unit Reads return to the discovery critical path;
- prefixed LN names are split from the first uppercase run rather than the numeric instance boundary;
- WYE/DEL/SEQ nested Data Objects are flattened into Data Attributes;
- descendant CF attributes inherit MX from a measurement parent;
- standard TCTR/TVTR/LTIM/EEName/MltLev objects are discarded because heuristic CDC inference is incomplete;
- Edition 2 LTRK tracking CDCs are emitted into Edition 1 SCL;
- case-distinct MMS/SCL member names such as LTRK `t` and `T` are collapsed by case-insensitive indexing or export trees;
- runtime RCB siblings are exported as separate logical ReportControl objects solely because mutable RCB settings differ;
- canonical save silently succeeds without reopen + association-plan validation.

## Physical acceptance

CI can prove source contracts, deterministic semantics, round-trip parsing, build integrity and portable smoke. It cannot prove IEDScout parity.

Production promotion remains blocked until AA1E1F06R4 is retested with the exact candidate artifact and produces:

- new PCAP;
- new Edition 2 IID;
- new Edition 1 ICD;
- diagnostic report;
- same-relay comparison against IEDScout.

The field result, not test count alone, decides whether the convergence target has been reached.


## R9 physical reuse lock — AA1E1F06R4

The R9 artifact (ARSAS `a89d6ef...`, engine `3e12fb9...`) established a split acceptance result that must not be flattened into a single pass/fail label.

**Locked as working and non-regressible**

- Smart Discovery remains one association and structure-first: 323 confirmed MMS requests, 138 GetNameList, 119 LN-root GVA, 2 GetNamedVariableListAttributes and 64 Reads.
- Edition 2 structural export reached 32 LD / 119 LN / 860 top-level DO / 906 DO+SDO / 4925 scalar leaves / 2 DataSets / 58 FCDA / 32 logical ReportControls / 1 SettingControl.
- Reopened Ed2 IID and Ed1 ICD both rebuilt the accepted association and matched 32/32 MMS domains.
- All trusted-SCL initial FC-root Reads completed on both editions (Ed2 563/563; Ed1 562/562).
- Both editions preserved 2 static DataSets and the two configured reporting plans (Digital BRCB + Analog URCB), resolved 58/58 runtime points with 0 unavailable points, disabled cyclic process polling, and received actual InformationReport traffic.

**Still open and must not be marked converged**

- Both editions still report exactly 46 initial FC projection errors. This is a semantic SCL/MMS shape problem, not a transport/association problem; target is zero.
- Ed2 projected 4250 leaves but cached only 4239. The exact 11-leaf loss matches the previously isolated LTRK case-distinct `t` / `T` collapse. The source fix is present but remains pending physical retest.
- R9 emitted 30 ADD preallocated URCB slots without a DataSet and with concrete runtime `...01` names. Never invent a DataSet. rptID-backed singleton slots must export as indexed logical ReportControl with `RptEnabled max=1`; unassigned indexed slots are warnings, not fatal missing-DataSet errors.
- R9 exported zero instance `<Val>` elements. Save-time bounded enrichment is a separate explicit phase and must not reintroduce eager FC-root Reads into Smart Discovery.
- Template deduplication remains secondary and must not trade away semantic correctness.

Future SCL-assisted diagnostics must include representative projection-error details (root + mismatch) so the remaining 46 errors can be fixed from direct evidence rather than inferred from a summary count.
