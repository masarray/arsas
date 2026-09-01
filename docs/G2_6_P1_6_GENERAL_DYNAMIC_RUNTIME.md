# G2.6 P1.6 — General Dynamic RCB Runtime Restoration

## Goal

Restore the original ARSAS Smart Auto acquisition contract for an IED/profile whose dynamic reporting mechanism is already physically proven:

1. use safe configured static report coverage first;
2. use bounded Dynamic RCB/DataSet coverage for every still-uncovered selected signal that resolves exactly in the current live MMS directory;
3. leave only genuine unsupported/unmapped residuals on MMS polling.

The Q0/A3 physical NO-GI `reason=data-change` proof is a **capability witness**, not a permanent two-member runtime whitelist.

## Physical capability basis

The reviewed field identity remains hard-bound to the existing P1.5/P1.5b manifest:

- stable identity: `ied:AA1C1F08R4`
- exact current model fingerprint/profile revision from the persisted qualification profile
- physically proven dynamic reporting on exact field URCB `AA1C1F08R4ADD/LLN0.RP.A_URCB01`
- actual spontaneous NO-GI `reason=data-change` InformationReport
- exact Q0 CSWI/XCBR member-index mapping
- association healthy after report
- mandatory cleanup succeeded
- reconnect/re-arm was physically observed in normal runtime

P1.6 does not alter or broaden that stored evidence. It changes what that evidence means for runtime policy: it proves that this exact IED/profile can safely execute the dynamic DataSet + RCB mechanism, while member eligibility is re-derived from the current live MMS model for each monitoring plan.

## Runtime planning contract

For the reviewed field-capable identity:

```text
selected signals
    -> safe configured static RCB coverage
    -> exact-resolved residuals
    -> bounded Dynamic DataSet groups
    -> freshly exact-verified free Dynamic RCB slots
    -> RptEna / event-driven reporting
    -> MMS polling only for genuine residuals
```

Dynamic grouping remains bounded by `MaxDynamicMembersPerReport` and `MaxDynamicReportPlans`. A signal is eligible for general dynamic coverage only when the normal ARIEC hybrid planner can resolve it exactly against the current live MMS directory and its functional constraint is compatible.

## Multi-RCB execution stability

ARSAS revalidates each report segment immediately before mutation. Plan-order names such as `AR_HYB_01` / `AR_HYB_02` are therefore not stable enough when multiple dynamic RCBs are active in the same logical device.

ARIEC PR #105 makes the temporary DataSet identity deterministic per exact RCB:

```text
AR_HYB_<SHA256-prefix-of-normalized-RCB-reference>
```

The same RCB therefore receives the same temporary DataSet reference during full planning and isolated pre-write revalidation, while different RCBs receive different names.

## Engine pin

ARSAS P1.6 pins merged ARIEC PR #105:

`4d7a896c606194c5533322bf975a2c9c57da7c64`

This includes:

- PR #104 — field-proven general Dynamic RCB runtime;
- PR #105 — stable multi-RCB dynamic DataSet identity.

## Safety boundaries retained

P1.6 retains all of the following:

- exact identity / fingerprint / profile-revision binding through the reviewed compatibility registry;
- successful activation + actual InformationReport evidence requirement;
- physical NO-GI dchg witness with exact mapping and cleanup;
- fresh current-association RCB availability checks before dynamic writes;
- static reporting precedence;
- bounded dynamic group/member limits;
- exact live MMS member resolution;
- process-lifetime dynamic-write circuit breaker after real activation failure;
- best-effort cleanup and reconnect/revalidation;
- MMS validation/fallback beside reporting.

P1.6 does **not**:

- rewrite or save the qualification profile;
- call `MarkProductionEligible`;
- synthesize strict shadow quality/timestamp evidence;
- weaken the separate ProductionEligible certification gate.

`field-capability runtime PASS != ProductionEligible`.

## Normal field validation

Use only the normal operator workflow:

```text
Open SCL -> Connect IED -> Start Monitor
```

Do not use commissioning/shadow hotkeys and do not requalify the profile.

Expected evidence for the field IED:

- P1.6 field-capability runtime candidate accepted;
- configured static RCB coverage remains active where useful;
- residual exact-resolved signals are partitioned into one or more Dynamic RCB groups, not restricted to Q0;
- each dynamic group has a stable `AR_HYB_<hash>` DataSet reference;
- `RptEna=true` / dynamic report monitor active for each successful group;
- spontaneous data-change reports update rows event-driven;
- MMS polling remains only for genuinely unresolved/unsupported residuals or runtime degradation;
- disconnect/reconnect causes fresh availability validation and safe re-arm without repeated mutation loops.

## Merge gate

PR #230 remains draft/unmerged until the P1.6 exact-head build passes CI and the physical normal-runtime all-signal field run is reviewed. ProductionEligible remains a separate later gate.
