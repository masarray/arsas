# G2.7 P1.7 — Native Per-IED Dynamic RCB Field Capability

## Field finding that triggered P1.7

P1.6 restored general Dynamic RCB member coverage for the reviewed legacy field identity `AA1C1F08R4`, but a second physical IED exposed the remaining product gap.

On `AA1E1F03R3`, normal monitoring discovered a healthy MMS association and advertised dynamic-report capability including Write, DefineNamedVariableList/DeleteNamedVariableList and 30 free URCB slots. Nevertheless all 139 selected signals stayed on MMS polling because no persisted identity-compatible dynamic qualification profile existed for that stable IED identity.

That behavior was fail-closed but incomplete as a product workflow: P1.6 generalized **member scope**, not **per-IED capability establishment**.

## P1.7 objective

Allow a previously unseen physical IED to establish its own durable Dynamic RCB capability witness without copying another IED's evidence and without weakening the ProductionEligible boundary.

P1.7 therefore adds an explicit per-IED commissioning bootstrap:

```text
exact selected IED identity
    -> G2.3 bounded dynamic DataSet envelope qualification
    -> G2.4 transactional free-URCB activation + actual InformationReport proof
    -> G2.5 strict dchg-only physical InformationReport proof
    -> monitor cleanup + proof-field restore + fresh-association closure
    -> persist native DataChange InformationReportProven profile
    -> persist separate identity/profile/activation/report/cleanup sidecar witness
    -> later normal Start Monitor reloads both
    -> static precedence
    -> all exact-resolved residuals may use bounded fresh Dynamic RCB groups
    -> genuine residuals only remain on MMS polling
```

## Explicit operator action

P1.7 bootstrap is never invoked by normal startup, Connect, Start Monitor or reconnect.

Select the physical IED and press:

```text
Ctrl+Shift+B
```

The action issues **zero automatic process/control commands**.

For a new IED, stages G2.3 and G2.4 run using the existing guarded commissioning transactions. During the final G2.5 phase, wait for the status marker indicating the strict dchg-only report path is armed, then cause exactly one already-approved safe physical/status change affecting one of the proven members.

If any qualification, activation, mapping, association-health or cleanup gate fails, P1.7 stops fail-closed and normal monitoring remains static/MMS fallback.

## Durable authorization is two-part

A native `InformationReportProven` profile whose report kind is `DataChange` is necessary but deliberately insufficient.

Normal P1.7 runtime also requires a separately persisted `MmsDynamicReportNativeFieldCapabilityEvidence` sidecar bound to:

- exact stable identity key;
- exact model fingerprint;
- exact profile revision;
- exact RCB reference;
- exact temporary DataSet reference used by the physical dchg proof;
- exact persisted RCB activation evidence ID;
- exact persisted InformationReport evidence ID;
- exact included DataSet member mapping;
- actual NO-GI data-change report evidence;
- healthy association after the report;
- successful monitor cleanup;
- successful TrgOps/OptFlds proof-field restore;
- successful fresh-association cleanup closure.

The ARIEC P1.7 policy revalidates the profile and sidecar together. A stale sidecar, copied sidecar, DataSet mismatch, evidence-ID mismatch, fingerprint/profile-revision mismatch, or incomplete cleanup cannot authorize general Dynamic RCB runtime.

## Runtime semantics after bootstrap PASS

The physical commissioning DataSet is a **mechanism capability witness**, not a permanent runtime member whitelist.

Normal runtime still derives each monitoring plan from the current live association:

1. safe configured static report coverage first;
2. current selected residual signals must resolve exactly in the live MMS directory;
3. only freshly exact-verified free Dynamic RCB slots may be used;
4. grouping is bounded by `MaxDynamicMembersPerReport` and `MaxDynamicReportPlans`;
5. every Dynamic RCB receives deterministic `AR_HYB_<hash>` temporary DataSet identity;
6. execution revalidation repeats the P1.7 policy and fresh availability checks immediately before mutation;
7. real activation failure opens the existing process-lifetime Dynamic-write circuit breaker;
8. genuine unresolved/unsupported/degraded residuals remain on MMS polling.

## Certification boundary

P1.7 bootstrap does **not** call `MarkProductionEligible`.

After a native capability PASS the profile remains `InformationReportProven`, now with an actual native `DataChange` proof. `ProductionEligible` remains a separate later certification gate requiring its own physical regression contract.

## Physical validation after bootstrap

After `Ctrl+Shift+B` reports PASS:

```text
Disconnect
-> Connect
-> Start Monitor
```

Expected normal-runtime evidence:

```text
P1.7 native per-IED field-capability runtime candidate loaded
Dynamic groups=N
Dynamic signals=N
MMS fallback=R
RCB=...
DataSet=AR_HYB_<hash>
members=...
```

For the `AA1E1F03R3` field case, the previous baseline was:

```text
requested=139
dynamicBRCB=0
dynamicURCB=0
polling=139
freeURCB=30
dynamicAllowed=True
```

P1.7 is accepted only when the later normal-runtime run physically demonstrates non-zero Dynamic RCB coverage for eligible exact-resolved points, successful activation/reporting, bounded genuine MMS residual only, and clean disconnect/reconnect revalidation. PR #230 remains draft/unmerged until that evidence is reviewed.

## Engine pin

P1.7 uses merged ARIEC PR #107:

```text
c979206988ebcbaf79e62b784895e19547184369
```

This retains P1.6 PR #104/#105 general-member and deterministic multi-RCB DataSet identity behavior while adding native per-IED capability authorization.
