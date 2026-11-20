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

## Follow-up field result — 2026-11-20 P9 trial

Observed source `b5b964f9` + P9 engine `58d22be4` actually ran in the uploaded trial and established **4/4 selected RCB routed Report ingress** (16 frames across four independent streams), 125/125 visible *selected* points, 0 observed sequence discontinuities on the sequenced streams, and two confirmed operator-initiated enhanced-SBO positive control terminations with process feedback. The two click-to-result latencies were about 556/512 ms; most of the observed command service interval included an explicit asynchronous negative-application-error observation grace. **Do not shorten this guard as a performance fix.**

Every selected BRCB advertised `BufOvfl=true` on the first report frame; later clean frames do not prove SOE historical completeness. Preserve severity and recommend a separate controlled buffered replay/continuity acceptance test, not autonomous PurgeBuf/GI.

The uploaded IID was parsed as 39 DataSets, 35 ReportControl templates, 16 declared enabled slots per template. **Exactly 33 DataSets had a ReportControl `datSet` binding within their own logical-device LN0; six lacked one in that IID.** This is configuration evidence, not live availability. Do not set 39/39 physical reportability as a success criterion.

The P9 Smart Discovery snapshot observed 560 distinct live RCB instances (400 BRCB, 160 URCB), while the existing enrichment budget was just 64 RCB reads. Original candidate selection prioritized buffered and sorted numeric instances so that four 16-slot families could consume the budget. That accounts for four proven live DataSet binding families and 35 unverified DataSets even though many other RCB instances exist. Four GetNameList chains remained partial, so exhaustive inventory is not independently proven. The selected four targets were already resolved, correctly avoiding the 192-page unresolved-only retry.

The follow-up engine trial is **ARIEC61850 PR #203**, an independent descendant of P9 #200. It performs family-balanced round-robin selection **within the same 64-read budget**, uses only exact observed RCBs, and still requires exact live DatSet and state readback before monitor activation. Selected DataSet membership, phase-leaf expansion and RCB instance names must not be confused: 21 parent members in one selected measurement DataSet produced 53 scalar feedback leaves; 132 per-plan binding slots reduced to 125 unique displayed points due overlap. This is not 445/445 Report ingress.

The target IP appeared among the testing workstation's local interface aliases. Therefore the capture does not independently establish whether the MMS endpoint was hardware, an on-host simulator, or a local relay test service. State this qualification explicitly in any field claim. Do not save the raw IID, logs, workstation details or network inventory in the repo.

### Candidate gates

- GitHub engine .NET CI for exact `24077e48e393ce3dc2cd1409db6e96f27aa1e368`: run `44720030989` succeeded on second attempt. The first attempt failed on an unrelated timing-sensitive enhanced-SBO negative-application-error test; track flakiness independently, preserve guard.
- The ARSAS P9 Windows candidate must now be built for this **new exact engine SHA** while production engine pin stays `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`.
- Re-test on confirmed isolated field/simulator endpoint with original IID opened: observed RCB families, exact DatSet bindings, 33 configured design DataSets, report ingress per selected target, BRCB overflow/EntryID/sequence chronology, close/open feedback and no Toshiba regression. Never auto-expand user selection or send operations.
