# IEDScout Convergence Contract

## Product target

ARSAS has one active IEC 61850 convergence target:

1. **Discovery parity:** one accepted MMS association, structure-first bounded discovery, no supplemental legacy browse, no recursive per-leaf GVA storm, and physical performance comparable to IEDScout on the same relay.
2. **Canonical model correctness:** exact Logical Node identity, correct DO/SDO hierarchy, Functional Constraint ownership from evidence, complete standard semantic authority where proven, and no model deletion used as a safety mechanism.
3. **Saved SCL usability:** full-model IID/ICD must reopen in ARSAS, rebuild the exact accepted association plan, reconnect to the physical relay, and remain usable for bounded initial reads and static reporting.

The machine-readable authority is `evidence/iedscout-convergence-target.json`.

Current R8 model-repair engine authority: `e2cfcebf25b7b54bb6d6b6e28b9d060b6b1f7f2c`.

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
