# P0.7 Release Candidate Lock

This document freezes the current ARSAS consumer/runtime behavior as the **good baseline** that must be preserved while the remaining SCL semantic work is completed.

## Accepted physical baseline

Physical relay: `AA1E1F06R4`.

Accepted ARSAS consumer head: `0d0b9204d6637e3d62e2eee94000386ae43cd0e9`.

Accepted physical engine baseline: `648124097621046f5f127ceb1cf853fea54db730`.

The following behavior is now non-regression authority:

- Static DataSet: **58 / 58** live rows.
- Analog: **22 / 22**.
- Digital: **36 / 36**.
- Report-backed runtime rows: **58**.
- Cyclic MMS process polling: **0**.
- Actual InformationReport traffic is required.
- All twelve structured Analog phase/subphase rows remain distinct and publishable.
- Phase/subphase context remains visible in the operator surfaces.
- DPC feedback keeps `Dbpos` / `[DP]` semantics.
- SPC/SPS feedback keeps Boolean / `[B]` semantics.
- Operator vocabulary remains `True [1]`, `False [0]`, `Open [01]`, `Close [10]`, `Intermediate [00]`, `Bad state [11]`.
- Save SCL while monitoring is a snapshot/export operation and must not tear down or restart the active report session.
- Saved SCL must reopen and reproduce the same 58-row report-backed workflow.
- FAT Preview/PDF uses the same operator vocabulary as Live/Event Log while persisted raw evidence remains unchanged.

## Frozen acquisition boundary

The remaining work is **SCL semantic parity**, not a reason to redesign acquisition.

Until new physical proof explicitly supersedes this lock:

- do not alter Smart Discovery request shape or budget;
- do not reintroduce cyclic MMS process polling for Static DataSet rows;
- do not replace configured Static RCB authority with automatic dynamic DataSet/RCB writes;
- do not reduce the 58-member runtime inventory;
- do not stop/reconnect monitoring during Save SCL;
- do not infer type badges from names or numeric appearance.

Any future SCL or engine semantic patch must be evaluated **on top of this baseline**, not instead of it.

## Remaining SCL semantic parity work

Generated SCL must continue converging toward the actual IED model and the trusted independent interoperability reference SCL.

A known concrete gap is `CBClsCounter`: the current physical baseline may export/reload it as SPS/Boolean, while exact live TypeSpecification evidence says the status value is integer and therefore belongs to the INS/integer semantic family.

ARIEC61850 PR #139 at `9123c8aa1cc51a1e13c6750c0e1f8bee2a2de3b7` is the isolated semantic candidate for that correction.

That engine candidate is **not allowed to trade away** any accepted P0.7 behavior. Integration must rerun the complete ARSAS/R7 regression stack and then physically verify at minimum:

1. 58 / 58 live rows.
2. 22 / 22 Analog and 36 / 36 Digital.
3. 58 report-backed rows with actual InformationReport traffic.
4. cyclic MMS process polling = 0.
5. Discovery and Open SCL keep identical operator type/state presentation.
6. Save SCL while live remains non-disruptive.
7. saved SCL reopens as 58 / 58.
8. corrected SCL preserves `CBClsCounter` as INS/integer and does not introduce semantic regressions elsewhere.

## Release rule

This point is a **locked release-candidate baseline**, not permission to weaken gates.

Do not declare release readiness until SCL semantic parity is accepted. All work from here should be narrowly additive/corrective and must preserve this contract.

Machine-readable authority: `evidence/p0.7-release-candidate-lock.json`.
