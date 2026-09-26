# ARSAS maintainability audit — evidence baseline, 27 September 2026

This is a *reproducible repository-snapshot audit and engineering worklist*, not a claim that every code path has been dynamically tested. It implements the audit-first workstream in [#380](https://github.com/masarray/arsas/issues/380). The v1.6.40 installed-release field record remains authoritative for its tested physical behavior. Historical tag, binary, engine pin, numerical field evidence and source identity must not be rewritten.

## Audited identity and methodology

- Default branch `main` at `b897203fad3b9276492bd3f6220e6c86d14c59df`; recursive Git tree from the GitHub Git Trees API reports `truncated=false`.
- Inspected: path/size metadata for the complete tracked tree; selected large implementation and lifecycle owners; existing tests, architecture contract, release automation, contribution/security policy, and asset provenance manifest. CI at this exact main SHA has Build ARSAS and post-merge production verification successful.
- This audit does **not** prove that every source reference, runtime concurrency path, binary asset's rights, or every script's dynamic consumer was examined. Those are explicit downstream review gates, not completed findings.
- Inventory measurements are Git blob sizes, **not** lines of code or measured runtime complexity.

| Scope | Snapshot count / size | Interpretation |
| --- | ---: | --- |
| Tracked blobs | 1,031; tree not truncated | Complete current-tree path inventory, not historical commits |
| C# source and test files | 632 C#; 240 in `tests/` | Includes generated/partial files as tracked |
| `Services/` | 165 C#; ~2.20 MB | Multiple subsystem owners; not one homogeneous layer |
| `MainWindow*.cs` | 75 files; ~837 KB | Large partial-class presentation/orchestration surface |
| `Services/NativeIec61850Client*.cs` | 14 files; ~407 KB | Native client/association facade split across partials |
| `Services/Iec61850MonitorRuntime.cs` | 2,681 lines; ~134 KB | Owns per-IED session and report/poll lifecycle |
| GitHub Actions workflows | 28 | Separate release, site, CI and field-evidence entry points |
| `scripts/` files | 55 | Exact consumer graph not yet verified |
| Tracked image/font assets in manifest scope | 65 paths, 42 unique blobs | 23 cross-deployment duplicate relationships, not duplicate authorship |

## Confirmed findings and carefully scoped risks

| ID | Finding and evidence | Action / acceptance |
| --- | --- | --- |
| M01 — verified lifecycle defect, corrected | `Services/SclSafeTrialRunner.cs` constructed a disposable native client without explicit `DisposeAsync` on success, failure or cancellation. `NativeIec61850Client` owns an association/gates and implements `IAsyncDisposable`. | #420 introduced `await using` and a focused ownership test; merged as `3a2da5279faa7b87e355628ea84475661bf06527`. PR CI 9/9; actual merged-main CI 2/2 (1,320 app / 988 engine tests). No physical trial run was claimed. Production Discovery/reporting unchanged. |
| M02 — structural concentration, not a proved runtime bug | `MainWindow*.cs` spans 75 files, the native client spans 14, and the monitor core is 2,681 lines. A partial-class split alone does not create subsystem boundaries. | Map fields, callers, synchronization and tests before choosing *one* pure extraction. No repository-wide rename or blanket MVVM rewrite. |
| M03 — ownership ambiguity for future maintainers | There are two intentional classes named `Iec61850MonitorRuntime` in different namespaces. UI-facing facade (`Services/UiResponsiveIec61850MonitorRuntimeFacade.cs`) owns per-device operation gates/cancellation and feedback fences; service core owns per-IED sessions, client, report/poll pipeline. | Document the two-layer contract in the existing architecture index; make changes on the correct side. Never add another session authority to a view. |
| M04 — stale workstream handoff | `docs/WORKSTREAM_COORDINATION.md` still describes earlier FAT branches as active and presents old integration context without the 2026-09-27 stable baseline. | Label its preserved COMTRADE/FAT sections as historical and add a dated current-state header. Do not rewrite historical acceptance claims. |
| M05 — automation complexity, not yet an orphan verdict | 28 workflows/55 scripts; release has a documented canonical publisher and a separate create-only verified publisher. A filename or grep count cannot prove an entry point is unused. | Before deletion, build exact trigger/caller/output/permission map including workflow_dispatch, schedule, docs, tests and release provenance. Retire only validated orphans; preserve immutable stable tags/assets. |
| M06 — asset review incomplete | `docs/asset-provenance-manifest.json` covers 65 paths. Four static Inter font entries have documented technical provenance; 12 artwork paths still request origin/rights/visual review; 49 screenshot paths request capture-origin/sanitization review. | Review unique content with creator/source/license/sanitization evidence and propagate the decision to duplicate deployments. No automatic legal-clearance claim from hash or keyword matching. |
| M07 — main policy verification incomplete | `.github/CODEOWNERS` assigns stewardship; repository rulesets API returned no configured rulesets on this snapshot. Classic branch protection/access behavior was **not** verified by this audit. | Stage required checks and branch protection only after confirming bot-driven website/evidence writes and maintainers' recovery access. No unreviewed gate changes. |
| M08 — existing controls are substantive | `AGENTS.md`, `docs/architecture/INVARIANTS.md`, `docs/architecture/PERFORMANCE_BUDGET.md`, immutable engine lock, exact-SHA CI, source-clean fixtures and release immutability tests already exist. | Extend these instead of creating parallel architecture policies or another release pipeline. |

## Ownership map — authoritative before refactoring

| Boundary | Existing owner | Must remain invariant |
| --- | --- | --- |
| WPF presentation | `MainWindow*.cs`, `IoListTestingWindow*.cs`, views | Bind/dispatch UI, do not own protocol truth or long blocking I/O |
| UI-to-runtime operations | `Services/UiResponsiveIec61850MonitorRuntimeFacade.cs` (root namespace) | Per-IED serialized operations, preemptive stop, stale-result rejection |
| Multi-IED session/acquisition | `Services/Iec61850MonitorRuntime.cs` (Services namespace) | One client/session per IED, static DataSet report-only = no cyclic polling/dynamic writes |
| Native application client | `Services/NativeIec61850Client*.cs` | Association-scoped client lifecycle, engine API delegation |
| Protocol engine | pinned `ARIEC61850` revision | MMS/reporting protocol logic, not duplicated in WPF |
| FAT session and evidence | `Services/IoTesting/` | Evidence integrity and persistence; schema compatibility |
| Release truth | `.release/windows.json` + canonical release workflow + immutable evidence | Never modify an existing published stable tag or artifact |
| Separate read-only trial | `Services/SclSafeTrialRunner.cs` | Local client disposed; no control/write/report enable side effects |

## Implementation sequence and exit criteria

1. **P0 — contain verified defects and publish truthful handoff.** #420 has merged and passed actual merged-main CI. Publish this dated audit/handoff through its separate PR and record its merged SHA in #380. Do not re-open physically accepted v1.6.40 as a new bug.
2. **P1 — complete provenance/release ownership.** Resolve 12 artwork and 49 screenshot-path reviews by unique blob. Build the complete workflow/script consumer map, including dynamic calls; classify, do not blindly delete. Confirm site/evidence bot permissions before deciding main protection.
3. **P2 — extract pure, testable components.** Start with a single reviewed candidate in `Services/IoTesting/IoTestProjectPersistenceService.cs` (snapshot mapping vs I/O vs autosave lifetime) or another measured pure component. First add serialization round-trip, old-project compatibility, cancellation/failure-path and lifecycle tests; preserve persisted bytes/schema and test fixture results.
4. **P3 — high-risk runtime seams only with evidence.** Review facade/core/client ownership; characterize reconnect, operation cancellation, shutdown and event subscription with deterministic tests and bounded timing before any extraction. Protect the accepted 58/58, Analog 22/22, Digital 36/36, 58 report-backed, zero cyclic polling, Save-SCL and reopen behavior. Physical tests only for changes in affected acquisition paths.
5. **P4 — presentation simplification.** Move a demonstrably pure projection/formatting operation out of a `MainWindow` partial into an existing service with caller and UI behavior tests. Keep event handlers thin; no giant rewrite.
6. **P5 — enforce selectively.** Introduce exact required checks/ownership protection through a staged bot-safe rollout. Avoid CI gates based solely on arbitrary file length. Each PR records changed files, base/head SHA, invariants, tests and remaining limitations.

A phase closes only with an actual merged commit, exact-head CI and recorded issue evidence. A successful build cannot substitute for physical field evidence when protocol/acquisition behavior changes. Existing release v1.6.40 is not repackaged by this program.
