# P9 follow-up: incomplete MMS inventory versus configured RCB absence

The user supplied a large BCUNR IID and a production-source diagnostic on 2026-11-20. Do **not** commit the raw IID or diagnostic report: they contain engineering configuration and workstation details. Independent sanitized findings:

- IID contained 39 DataSets and 35 ReportControl definitions, with each declaring 16 enabled RCB slots (potential indexed instances, not proof all are presently available).
- Live discovery reported 35,698 NamedVariables across eight domains, 288 observed RCBs and four incomplete GetNameList chains. At least one target domain stopped after 64 pages/6,400 names.
- A few static segments received six Report frames across three independent RCBs and showed seven of seven user-selected values. It is therefore inaccurate to claim *total MMS communication failure* or *IED has no RCB*.
- Original process used the pinned `f75e323e` engine, not the experimental P9 `58d22be4` engine.
- An independently reported reconnect was successful in 45 ms, but root cause of prior disconnection is unknown.
- User selection of seven values is not equivalent to total discovered static DataSet membership; never silently select hundreds of points or enable cyclic MMS polling.

## Repair contract

1. Do not infer absence from incomplete **live** discovery. Opened SCL missing-RCB evidence is scoped strictly to the opened design, not physical absence.
2. Preserve all counts and sample references in one bounded warning instead of emitting many identical claims. Never suppress actual BufferOverflow, RCB activation, or report routing warnings.
3. Initial static reconciliation stays at 64 pages and is read-only. If the association is live, the result is partial, selected exact target segments remain unresolved, and a **64-page NameList limit** was explicitly observed, allow exactly **one** targeted **192-page** read-only retry. A recovered RCB must still pass exact DatSet, eligibility, ownership and readback proof. No vendor-specific exceptions, cyclic read, GI loop or silent RCB changes.
4. The general Smart Discovery frozen configuration stays at 64 pages. Extra work is restricted to the active selected DataSet/RCB acquisition, never a broad login sweep.
5. Exact physical readiness and interoperability remain a field gate; CI and IID are not wire proof.

Known limitation: the production engine does not yet expose a typed GetNameList truncation reason; this minimal back-compatible consumer gate uses the engine's stable explicit diagnostic message. The retry re-enumerates only the selected target domains once; a later engine enhancement can resume pagination without re-fetching earlier pages if needed.
