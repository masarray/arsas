# ARSAS v1.6.42 — Five-IED operator FAT evidence and release decision

**Capture:** 2026-10-25 07:20:57 Asia/Jakarta. **Classification:** operator-supplied field diagnostic, *not* unconditional physical interoperability acceptance. This is a sanitized summary: the raw diagnostic includes local machine paths, interface aliases, private endpoints and native MMS hex, and must not be republished as a public repository asset.

## Immutable source and artifact provenance

- Operator executable assembly source SHA: `4c7d833a85437037418a5f4e1fbe2c08034b1581`.
- Integrated RC merge SHA at comparison: `3abde5fb34ed915a32ec60a4bacdf682ab1bddbf`.
- GitHub compare `3abde5fb...4c7d833`: **diverged by one merge commit each, 0 changed files**. Content-equivalent source; distinct git graph and binary artifact identities must still be recorded separately.
- ARIEC61850 pin from operator build: `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` (trial, not historically qualified R7/R10 stable engine).
- Version: `1.6.42`, .NET 8.0.32, Windows x64. This operator trace is not proof of exact Windows installer SHA256, code-signature identity, or final main commit.

## Live acquisition & typed command evidence

| Device | Connected/Monitoring | Static initial image | Routed exact RCB plans | Selected typed-operable | Independently discovered typed-operable CSWI | Discovery total |
|---|---|---:|---:|---:|---:|---:|
| BCUGE | yes/yes | 67/67 | 6/6 | 16 | 0 | 1,402.7 ms |
| GR_X_7SX85 | yes/yes | 13/13 | 2/2 | 2 (3 status-only) | 1/1 | 1,705.0 ms |
| SG7_BCU | yes/yes | 77/77 | 4/4 | 13 | 4/4 | 7,985.1 ms |
| SIEBCU | yes/yes | 18/18 | 1/1 | 4 (7 status-only) | 0/2 | 1,228.3 ms |
| ZIVAVR | yes/yes | 8/8 | 1/1 | 2 (3 unverified/read-only) | 0 | 2,167.9 ms |

Totals: 5/5 connected and monitoring; 183/183 selected signal initial images displayed; 14/14 individually planned static RCB routes had received ingress; *display completeness is not proof of per-member independently changing wire values, event continuity, or Open-SCL parity*. No cyclic process-value MMS polling. RCB static semantic gate is `COMPARABLE` but `AWAITING PEER INGRESS` for Open-SCL parity.

**SG7 type/scale:** 6 LD, 32,902 raw MMS names, 31,481 FC points, 281 live-model LN, 3,957 DO, 25,522 DA, 6 static DataSets, 48 RCB discovered, 422 discovery requests with **0 duplicates**, 7,985.1 ms end-to-end. Before claiming any discovery latency SLA, test repeat-run p95/p99 and machine load; this is one capture.

### Actual operator-driven commissioning and control (not just disabled-button inventory)

**SG7_BCU `SG7_BCUSystem/BkrCSWI1.Pos`:**
1. Operator intentionally changed exact CF ctlModel **Direct Enhanced → SBO Enhanced**. Native result: `writeAccepted=True`, `readbackVerified=True`.
2. After dialog closed, `CTL_MODEL_POSTWRITE_INSPECTION`: original MMS association=1, ctlModel SBO Enhanced, `typedReady=True`, `operable=True`; no indefinite “Reading…”.
3. Operator staged and confirmed **Open [01]**; native wire `SBOw → Operate → positive CommandTermination → process Open`. 2 ordered control responses captured, no-control-error; client total **448.089 ms**, engine **440.581 ms**. This timing includes configured ~400 ms SBO application-error observation, not all network RTT.

**GR_X_7SX85 `GR_X_7SX85CB1/CSWI1.Pos`:**
1. Operator ctlModel **Direct Enhanced → SBO Enhanced**, exact MMS write accepted and readback verified.
2. Post-Write association=1 typed inspection `typedReady=True`, `operable=True`.
3. Operator **Open [01]** succeeded: positive CommandTermination + Open feedback, client **411.678 ms**. Subsequently **Close [10]** succeeded: positive CommandTermination + Closed feedback, client **412.975 ms**.

These are strong bounded proof for the listed control objects and actual actions only; not blanket manufacturer certification, relay trip-safety, other controls, reconnect, or all enhanced-security failure paths. Every operable path retains typed CDC/status semantics, association fences and native single-flight authorization.

### Open continuity concerns (do not silently pass release gate)

| Device | Observed frames | Continuity findings | Advertised BufOvfl |
|---|---:|---:|---:|
| BCUGE | 10 | 6 | 3 |
| GR_X_7SX85 | 3 | 1 | 0 |
| SG7_BCU | 391 | 24 | 4 |
| SIEBCU | 13 | 4 | 1 |
| ZIVAVR | 10 | 1 | 1 |
| **Total** | **427** | **36** | **9** |

Findings are sequence/discontinuity/metadata **observations**, not 36 confirmed lost events. BufOvfl is affirmative evidence of the IED advertising buffer overflow; it cannot be reported as harmless. Consecutive clean frames do not prove lossless buffered replay, SOE time ordering or reconnect completeness. **R7/R10 physical acceptance, controlled generation/replay/wire-order evidence and original SCL/IID parity remain outstanding.**

Additional limitation: the diagnostic warns that some target IP aliases are assigned locally to the capture workstation and may represent same-host simulators; do not describe all five endpoints as independent hardware. ZIVAVR reports one initial non-scalar `swRev` read failure; its 8 selected report values are still displayed. SIEBCU has two additional independent CSWI unresolved/nonoperable. Explicitly retain these vendor-specific exceptions.

## CI evidence and decision

- Exact feature-source test SHA `cd970af2217d0ca393e782aec46809034032a5a5`: Windows Build [40144472595](https://github.com/masarray/arsas/actions/runs/40144472595), SCL R7 [40144472579](https://github.com/masarray/arsas/actions/runs/40144472579), Reference Guard [40144472582](https://github.com/masarray/arsas/actions/runs/40144472582) **pass in trial/PR validation context**.
- Integrated RC `3abde5fb` has push-context Build [40144991729](https://github.com/masarray/arsas/actions/runs/40144991729), SCL R7 [40144992276](https://github.com/masarray/arsas/actions/runs/40144992276) and Reference Guard [40144992176](https://github.com/masarray/arsas/actions/runs/40144992176) **success**; separate PR-context R7 [40144991736](https://github.com/masarray/arsas/actions/runs/40144991736) **failed** with `Unqualified typed-control staging pin cannot replace physical R7/R10 authority`, and Reference Guard [40144991734](https://github.com/masarray/arsas/actions/runs/40144991734) **failed** with `Smart interoperability code-trial authority is malformed or was silently promoted`. This is an intentional conflicting qualification gate, **not all green**.
- The source-equivalent operator test plus successful functional CI therefore supports **RC/FAT status**, *not* a public stable promotion. Do not change `.release/windows.json` or `.release/published.json`, retarget to historic stable engine, force-merge PR #729, or tag `v1.6.42` stable until gate owners sign off the exact-pinned physical evidence.
- Prior public stable **v1.6.41** stays published and unchanged.

## Final stable release steps (gated)

1. Preserve this sanitized operator evidence and reference its source-equivalent build; separately record the exact installation asset SHA256/SBOM/provenance.
2. Qualify R7/R10 with required same-pin physical evidence across three target IEDs (including ZIV BSC, report/SOE buffered recovery, reconnect and session quarantine); reconcile PR-vs-push CI event rules **without weakening acceptance predicates**.
3. Confirm no intended code changes beyond tested RC; require all **required** checks green on the *identical main promotion source* and independent reviewer/maintainer approval.
4. Only then merge the release-gate PR #729, publish Windows installer+portable with checksum and SBOM via canonical `release-windows.yml` (never manual ad-hoc artifact rename), verify install/uninstall smoke, signatures, public download and rollback.
5. Retain the v1.6.41 stable artifacts and immutable historical PRs; close only fully superseded stacked PRs with explicit traceable reasons and link to the RC integration.
