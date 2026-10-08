# Engineering handoff — SCD ServerAt / AccessPoint provenance (P7.6A)

**Status snapshot:** 2026-10-08, Jakarta time. **This document is the coordination entry point for issue [ARSAS #446](https://github.com/masarray/arsas/issues/446).**  
**Implementation:** P7.6A **CodeVerified / CI PASS**, **not PhysicalVerified for J↔F AP switching, not merged into main, not publicly released**.  
**Last fully CI-verified *code* commit:** [`1384185a1f42bcf2901e9be0f730863813420ba4`](https://github.com/masarray/arsas/commit/1384185a1f42bcf2901e9be0f730863813420ba4) on `fix/446-p7-6a-scl-ap-bound-endpoint-provenance`. A documentation-only commit after this checkpoint must not be mistaken for a newly qualified binary.  
**Pinned engine:** `ARIEC61850` exact `352c81e6a798635c6addcee0683235ca87ad416d` (engine [PR #153](https://github.com/masarray/ARIEC61850/pull/153); CI-only engine [PR #154](https://github.com/masarray/ARIEC61850/pull/154), **DO NOT MERGE #154**).  

## 1. Read this before coding

The user's real Siemens-family station SCD has a single IED with multiple AccessPoints: J declares the `Server`; F references J's server model with `<ServerAt apName="J"/>`; communication information can differ by AP. An **on-host local MMS test server** bound to a local host address is an intentional valid test setup, not inherently an invalid network conflict. A station SCD can legitimately be offline-only; do not infer MMS endpoints from GOOSE/SV addresses.

**Evidence caveat:** a prior inspection of one station SCD version showed two 8-MMS `ConnectedAP` addresses, while later field diagnostics showed `SCL_MMS_IP_MISSING` and connected via prior successful endpoint history. **Do not silently reconcile these into one claim**. Confirm the exact source file/version/SHA used by each run, parsed `ConnectedAP` identities, selected AP, IP provenance, and any runtime override. This discrepancy is an explicit follow-up, not an excuse to rework the already-proven association path.

A subsequent *real local-server Open SCD trial* reported:
- 33/33 expected MMS domains matched; initial FC reads 620/620; projection errors zero;
- 13/13 selected configured static DataSet values displayed, 0 pending; 2/2 configured BRCB/URCB report routes received;
- four control feedback transitions were confirmed;
- no full discovery and no cyclic MMS process polling in the selected static report-only path.

These measurements prove **that trial path only**. They do **not** prove AP F live connectivity, complete event continuity, all quality values Good, or Discovery-vs-Open-SCL parity. No customer SCD, private IP inventory, or diagnostic logs should be committed to a public PR. Only use synthetic RFC 5737 test addresses in fixtures.

## 2. Authoritative branch/PR chain (do not rebase onto an older release)

| Project | PR | Head / base | Responsibility |
| --- | --- | --- | --- |
| ARSAS | [#425](https://github.com/masarray/arsas/pull/425) | `main` | Engine-owned SCL-assisted connection groundwork |
| ARSAS | [#454](https://github.com/masarray/arsas/pull/454) | #425 | P7 unified static acquisition |
| ARSAS | [#459](https://github.com/masarray/arsas/pull/459) | #454 | P7.1 one IED card, endpoint restore, diagnostic resilience |
| ARSAS | [#466](https://github.com/masarray/arsas/pull/466) | #459 | P7.2 association lifecycle |
| ARSAS | [#471](https://github.com/masarray/arsas/pull/471) | #466 | P7.3 static ingress parity evidence |
| ARSAS | [#473](https://github.com/masarray/arsas/pull/473) | #471 | P7.4 causal BRCB/URCB traffic proof |
| ARSAS | [#476](https://github.com/masarray/arsas/pull/476) | #473 | P7.5 SCD J/Server, F/ServerAt dual MMS AP chooser |
| ARSAS | **[#480](https://github.com/masarray/arsas/pull/480)** | **#476** | **P7.6A IP-independent AP chooser, exact-AP source/provenance and concurrency** |
| Engine | [#153](https://github.com/masarray/ARIEC61850/pull/153) | `ci/144-smart-interop-integration` | P6.2 resolves ServerAt owner for offline, domain inventory and initial FC read |
| Engine | [#154](https://github.com/masarray/ARIEC61850/pull/154) | `main` | CI-only test route; **DO NOT MERGE** |

Everything above is a **stack of draft PRs, not merged main/release**. Preserve the exact engine SHA/lock provenance and historical physical baseline. Treat `main` or the public release as **older than this stack** until formally promoted; don't start a new milestone from the public release or revive retired web UI. Target is the **native Windows ARSAS Studio GUI**.

## 3. P7.6A work actually implemented

- `Services/SclEndpointTopology.cs`: `Choices(document, iedName)` enumerates **offline-browsable logical AP identities** independently from IP; resolves exact IED/AP/address and rejects ambiguous/forged candidates. `Candidates` remains the direct-IP-only view.
- `Services/SclEndpointBindingStore.cs`: successful MMS bindings are scoped to **SCD source SHA256 + IEDName + APName**; serialized in a separate user-local preferences file using lock + same-directory atomic replacement; no remembered authority if corrupt. Only record after native SCL-assisted association succeeds.
- `MainWindow.SclEndpointChoices.cs`: the native WPF right-click AP selector shows APs with and without SCD IP. Switching is **offline**, device-local `IsBusy` prevents overlapping Play/Connect All / AP reload, and checks exact source SHA, IED/AP/endpoint state after awaited reload. No auto-connect, silent failover, process reads or RCB writes.
- `MainWindow.xaml.cs`: selected AP and IP binding no longer mix; switching AP clears previous AP's address; exact binding can restore for *that* AP; legacy IED-wide successful-IP hint is allowed only on **initial SCD import**, **not** on switching. Unbound Play opens a blank IP entry, not an unrelated "New IED" IP. Reopen project restores full AP choices from **one SHA-checked SCD parse**. Successful SCL association writes exact AP binding.
- `Models/MonitorModels.cs`: AP inventory, direct-IP candidates and `SclEndpointOrigin` are distinguishable in presentation.
- `Services/DiagnosticReportBuilder.cs`: normal and emergency diagnostics include all AP choices, missing direct IP and selected endpoint provenance; legacy endpoint-only diagnostic models remain compatible.
- `tests/ARSAS.Tests/SclAccessPointProvenanceP76Tests.cs`: synthetic J/F no-IP/mixed-IP fixtures, forged AP rejection, source/IED/AP isolation, concurrent remembered-binding updates, corrupt-state rejection, native no-auto-connect assertions.
- Existing tests from P7.5, including `SclServerAtEndpointSelectionTests`, remain green.

**Thread safety:** isolation is per IED for online operations; a single local binding-history file is synchronized for in-process concurrent writes; only a successful exact association creates a remembered binding. This is not a claim of a distributed cross-process lock.

## 4. Exactly verified CI / artifacts (code SHA above)

**All 11 GitHub Actions workflows for code head `1384185a...` completed successfully**. Required gates: `Build ARSAS`, `Smart Discovery Field Capture Build`, `SCL Interoperability R7 Build`, `Smart Discovery Merge Execution Guard`, `Smart Discovery Production Promotion Guard`, `Smart Discovery Mainline Readiness`, `IEC 61850 Interoperability Reference Guard`, Golden Provenance, Golden Budget Lock, Repeat-Run Stability, and SV evidence bundles.

- [Build ARSAS #? — run 37765704216](https://github.com/masarray/arsas/actions/runs/37765704216): SUCCESS; [portable x64 single-EXE artifact **11544118143**](https://github.com/masarray/arsas/actions/runs/37765704216/artifacts/11544118143). [Regression evidence **11544592041**](https://github.com/masarray/arsas/actions/runs/37765704216/artifacts/11544592041).
- [Merge Execution Guard run 37765704174](https://github.com/masarray/arsas/actions/runs/37765704174): SUCCESS; **1,359 passed, zero failed, zero skipped**.
- [R7 run 37765704190](https://github.com/masarray/arsas/actions/runs/37765704190): SUCCESS; [R7 Windows x64 artifact **11543579473**](https://github.com/masarray/arsas/actions/runs/37765704190/artifacts/11543579473).
- [Field Capture run 37765704171](https://github.com/masarray/arsas/actions/runs/37765704171): SUCCESS; [field-capture Windows x64 artifact **11544048624**](https://github.com/masarray/arsas/actions/runs/37765704171/artifacts/11544048624).
- Engine [P6.2 .NET CI run 37759856930](https://github.com/masarray/ARIEC61850/actions/runs/37759856930): SUCCESS, engine SHA `352c81e...`.

**CI passing ≠ field acceptance or permission to merge.** Promoting to main requires verified ancestry and explicit physical qualification; CI "production promotion guard passed" means its restrictions were honored, **not** that production was promoted.

## 5. Field acceptance still missing (do not report as completed)

1. Open the **exact SHA-versioned** real SCD used for each test. Record an evidence matrix per IED/AP: `Server` vs `ServerAt` owner, `ConnectedAP` address (may be absent), source SHA and runtime binding origin. Reconcile file-address-vs-diagnostic discrepancy.
2. One card per IED. J and F both visible in native chooser even if no IP. Select F while offline; **J IP must never transfer**. If F has no exact previously proven binding, show `No endpoint`, prompt explicit IP on Play.
3. Perform a fresh association with a suitable F endpoint **only if that endpoint is actually reachable**. Do not fabricate F IP or claim F verified by J success. Then switch J→F→J with disconnections: exact successful binding restoration, no stale point values, no cross-IED changes or concurrent operation races.
4. Save and reopen project; verify AP inventory/source SHA/selection. Edit a *copy* of the SCD after import: reloading must block source-drift use.
5. Check actual BRCB/URCB InformationReport ingress, GI/startup images, quality, freshness and SOE continuity; **never** cyclic MMS polling or dynamic DataSet writes in explicitly static report-only mode; control commands retain the proper feedback fences.
6. Run **both** Discovery IP and Open SCL on the **same** physical/live IED for P7.6B. Compare canonical LD/LN/FC identities, ordered DataSet members, RCB binding, selected-point resolution, control model and acquisition intent. Store full causal evidence; do **not** claim parity simply from matching counts or `13/13 displayed`.

## 6. Recommended next execution sequence

**P7.6A** code/CI DONE; **P7.6A physical J/F switching qualification OPEN**. Do not start by refactoring reporting or redoing ServerAt fixes. If a physical issue appears, use the same PR stack/engine pin and add a narrowly scoped regression before changing code.

**P7.6B** next feature milestone: Discovery/Open-SCL canonical-model and static-report parity proof; exact same IED/source and selection, replayable normalized evidence, live dual-ingress runs. Then **P7.6C** BRCB/URCB sequence, ConfRev, BufOvfl, EntryID, freshness and SOE continuity; **P7.6D** multi-IED lifecycle / stale callback / reconnect race protection; **P7.6E** diagnostic truthfulness and release-qualification matrix. Separate model comparison from traffic and user-visible value completeness.

Before editing: read [issue #446](https://github.com/masarray/arsas/issues/446), [PR #480](https://github.com/masarray/arsas/pull/480), [PR #476](https://github.com/masarray/arsas/pull/476), [engine #153](https://github.com/masarray/ARIEC61850/pull/153), the exact `engines/ARIEC61850.lock.json`, CI artifacts, and the current PR head. Consult other active threads/PRs to avoid modifying overlapping files without reviewing diffs. Prefer stacked drafts, small testable changes and evidence-backed progress reports.

### Non-negotiable invariants

1. **Native WPF ARSAS Studio**, not the old web application.
2. ARIEC61850 owns IEC-61850 wire protocol / ServerAt resolution / RCB semantics; ARSAS is a consumer and must not implement a second incompatible protocol stack.
3. **Single IED card, multiple separately bound AccessPoints**, no vendor-name hacks, no source SCD mutation, no guessing endpoint from GSE/SMV, no treating a legitimate local MMS test server as a network conflict.
4. **Static DataSet report-only** means configured BRCB/URCB; no cyclic MMS fallback or synthetic `Good` quality, no dynamic dataset mutation.
5. **Multi-thread aware:** no global mutex blocking independent IED sessions, no state leakage after AP switch, no stale callbacks from old associations; guard per-device operations and source SHA.
6. **Evidence hierarchy:** CodeCommitted → CIPassed → NativeArtifactAvailable → PhysicalVerified → MergeReady → Merged → Released. Do not skip gates or confuse a draft PR with public release.
7. **Do not silently merge**, rebase onto an old release, overwrite another thread's branch, repin engine from a different baseline, or publish customer SCD/IP data.

