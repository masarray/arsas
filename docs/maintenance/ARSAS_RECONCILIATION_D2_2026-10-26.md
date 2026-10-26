# ARSAS — Reconciliation D2: Report DataRef and SCL Initial-Evidence Lineage
**Date:** 2026-10-26 (Asia/Jakarta)  
**Previous:** [D1 checkpoint](ARSAS_RECONCILIATION_D1_2026-10-26.md)  
**GitHub audit-only branch:** `audit/arsas-reconciliation-d1-unique-delta-20261026`; preserves other maintainers' audit branches unchanged.

## Final live outcome
- D1 entry **133 open PR / 17 open issues**, after #494 **132 / 17**.
- D2: **132 → 130 open PR**, **17 → 17 open issues**. No new PR/issue, no merge, no branch deletion.
- **Three total closures this thread:** [#494](https://github.com/masarray/arsas/pull/494) (D1), [#585](https://github.com/masarray/arsas/pull/585), [#604](https://github.com/masarray/arsas/pull/604). Original branches remain; source/test assets are recoverable.
- ARSAS main unchanged `14e5dddfbaae012b61a021366c1a864d83d316f2`; ARIEC61850 main unchanged `3d21238d0b62f24faecd5948a4ee238c82b35966`; accepted public v1.6.42 not modified. Draft integrated RC remains `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`.
- RC lock `engines/ARIEC61850.lock.json` (blob `eacaf68a680dcb89b7604a8618f63d521a19bfba`) **explicitly pins trial engine** `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`, `sourcePullRequest:192`, purpose draft/code-green NOT physically accepted; its textual `ref: main` is not license to substitute current engine main.
- No Windows build or workflow rerun initiated by this cleanup.

## D2 closure #585 — exact raw InformationReport DataRef/FC guard retained
[Individual five-field disposition + evidence](https://github.com/masarray/arsas/pull/585#issuecomment-6406096346).
- PR old `02ed1e3b33abd281e7d59513faaf1b11c4ef58ec` → RC: **ahead 1358, behind 0**, merge-base original.
- All two changed paths compared. `tests/ARSAS.Tests/M214ReportQualityDataRefIdentityTests.cs` **identical blob** `60e5d102cb58a8484345a1fafb84202ef5a863c4`.
- Original `Services/IedBrowserReportWireEvidence.cs` `9b46d1701a23343fb1dc9b9b0ed3b41aa0cdb299`, RC `9a882fa938c1d8cb2aa875d521a087537b9ec21e`. The entire `DataReferenceAgreesWithMember` predicate and pre-decode call remain: canonical member identity and FC extracted from DataRef, fail-closed mismatch. Old `Extract(frame)` path now delegates to `Extract(frame, null)` keeping direct q/t and single-level structure behavior. Optional typed-model overload adds exact schema-routed nested q/t and declines ambiguous fallback.
- The exact previously unique test suite remains. Closure is not a statement about physical BRCB continuity, and #520/#544 remain open.

## D2 closure #604 — verified SCL initial values share canonical per-IED ledger
[Individual five-field disposition + evidence](https://github.com/masarray/arsas/pull/604#issuecomment-6406100963).
- PR old `5a83f4510bf7159c8534e5deb32bf41516ff563e` → RC: **ahead 1240, behind 0**, merge-base original.
- All three changed paths compared. `Services/IedBrowserInitialEvidenceProjection.cs` remains **exact** blob `511dac32c2b10e6d33c3383c1db7d64ba1adb7f6`; `tests/ARSAS.Tests/IedBrowserInitialEvidenceM217BTests.cs` remains **exact** blob `b66de8475b0908321da86109328740abf5ef426e`.
- `Services/Iec61850MonitorRuntime.cs` evolved from `6fe4e44f38ab40aa7ff6a65b63977d4933c97d12` to `0f783af40b2af82a462b9388e3a6d327188cfa9c`. Original SCL-assisted `PublishTrustedSclInitialEvidence(session)` call remains (RC line 850) and the gate remains (RC ~947): same active DeviceSession, connected client, trusted online SCL authority, matching live model object. Also safely reused at reconnect (~3212). No additional MMS connection or guessed process evidence.
- The regression file remains byte-identical. No claim of full static/physical interoperability acceptance; #729 source promotion remains physically gated.

## Explicit code/test risks (DO NOT CLOSE)
- **[#606](https://github.com/masarray/arsas/pull/606#issuecomment-6406057666):** missing 7-test historical file. Identity, SGCB read-only and RCB nested flags are largely covered by newer tests, but decoder-to-UI raw unbound allData completeness and wrong-domain SGCB authority require focused tracing and if necessary minimal test-only rescue. Old direct `OrderedWireEntries` API and old subscriber navigation are obsolete: never restore blindly.
- **[#615](https://github.com/masarray/arsas/pull/615#issuecomment-6406044017):** missing `Iec61850ControlRequestSnapshot`; current GUI provides WPF `SignalDefinition` alias directly, native can mutate it after await, late unresolved command can survive timeout and reconnect. Existing quarantine tests do not prove WPF model ownership isolation. Coordinate with active Control/Write owners.
- **[#502](https://github.com/masarray/arsas/pull/502) / [#503](https://github.com/masarray/arsas/pull/503):** unique historical C264 strategy/handoff evidence not present in RC; preserve branches and curate sanitized archive before any closure.
- **[#570](https://github.com/masarray/arsas/pull/570) and [#567](https://github.com/masarray/arsas/pull/567):** small phase/vector PRs were probed; each touches 3 source/test paths and **none** of those 3 blobs match current RC. Both have clean commit ancestry (old→RC ahead 1394 and 1400; behind 0) but require exact semantic and rewritten-test proof. No ancestry-only closure.
- #729, #727, #726, #716, #732, #688, #374, #435, #546, #543/#649 remain protected. Issues 17 unchanged.

## Exact-head CI reality
- ARSAS `main` at `14e5ddd...`: [Build ARSAS run 40204560933](https://github.com/masarray/arsas/actions/runs/40204560933) **success**.
- ARIEC main at `3d21238...`: [Build, test and public-source verification run 39771714908](https://github.com/masarray/ARIEC61850/actions/runs/39771714908) **success**.
- Current RC exact HEAD GitHub check-runs collection shows **mixed historical outcomes**, not universally green: successful Windows packaging and native bridge checks, but also failed earlier [SV proof job 121085813649](https://github.com/masarray/arsas/actions/runs/40165560046/job/121085813649) and [IO proof job 121085813574](https://github.com/masarray/arsas/actions/runs/40165559937/job/121085813574); successful other jobs with same SHA also present. Failed jobs specifically stopped at step 5 *Prove exact canonical ... domain tests passed* while offline fixtures succeeded. Before ANY canonical promotion, adjudicate required latest-attempt/context/status policy rather than counting a green job as full acceptance. No rerun initiated.
- Physical R7/R10, source promotion, trial engine and user-accepted v1.6.42 are **distinct**.

## Recommended next execution
1. Trace subscriber decoder invariants and add scoped #606 regressions only where truly uncovered. Do not publish raw GOOSE allData as verified FCDA or substitute SCL values.
2. Bring #615 alias-ownership finding to the active MMS Control/Write integrator; add a minimal command-only snapshot where ownership is proven and test old-session late mutation, without modifying acceptance path #726 or creating unreviewed duplicate PRs.
3. Sanitize/archive historical #502/#503 content, preserving actual SHA and provenance rather than treating planning docs as production evidence.
4. Continue source lineage cohorts using exact historical patch, current blob, test assertion, semantic review and safety gate; do not close #567/#570 without rewritten-test equivalence.
5. Keep original branches and sealed v1.6.42 untouched; never infer physical testing from CI. No unnecessary full Windows builds.

**Outcome:** total 3 evidence-backed closed historical PRs; 130/17 remaining; active and unique safety work retained with concrete blockers, checkpointed for thread handoff.
