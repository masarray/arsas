# Command Dock reliability and control latency — architecture diagnosis and redesign plan (2026-10-20)

Status: **draft, partial implementation, not field-qualified**. Workstream descends from command PR #635 -> #638, engine instrumentation PR #183. Independent leaf-data and IED-card-progress PR #636 MUST be preserved in the eventual combined candidate.

## 1. Facts and distinct latency classes

Physical operator diagnostic (ARSAS 1.6.40+99d39c77, engine ba21d882): three SBO enhanced controls completed with positive termination and matching final process feedback in **7,964 / 8,099 / 13,324 ms** click-to-result. The native control action consumed **7,808 / 7,973 / 13,224 ms**; post-action read of feedback took roughly **8–14 ms**. No evidence yet partitions native time among SBOw service, Operate service, and asynchronous CommandTermination.

A separate IEC 61850 client being visibly faster does not prove the physical IED CommandTermination is faster: clients may present **MMS service accepted**, **positive termination**, and **process feedback confirmed** at different milestones. Never substitute early acceptance for actual termination or feedback.

The baseline consumer has an avoidable unrelated **UI/MMS admission backlog**: Command Dock reopen/refresher inspection and 2.5-second preload may schedule reads onto `NativeIec61850Client._controlCommandGate`, shared with actual control.

## 2. Proven Command Dock flicker / unstable row sources

### Primary: whole-grid collection reset

`Iec61850MonitorDevice.RefreshCommandSignals` computed `DistinctOperable(Signals)` then ALWAYS called `CommandSignals.ReplaceAll(selected)`. `BulkObservableCollection.ReplaceAll` clears items and raises `NotifyCollectionChangedAction.Reset`, even when all selected objects are the exact same instances in the exact same order. This invalidates WPF row containers, scroll/selection/edit state. `PreloadControlModelsAsync` calls projection on **every 2.5-second tick even when there are no candidates**; Command Dock explicitly refreshes again after reads.

### Secondary: transient verification drives membership

`Iec61850ControlIdentity.DistinctOperable` conditions on `ControlModelResolved`, `ControlSupportsOperate`, and `IsGenericControl`. An `InspectControlCoreAsync` invalidates live control authority before rebuilding it. Other background projection updates can observe temporary unverified state and remove rows; later restore them. Correct approach: a committed association-scoped typed control proof, not a transient partial validation object, should authorize an operation. **Never let stale metadata authorize a write.**

### Tertiary: polling-based visual mutation

`MainWindow.CommandPanelUx.cs` runs a timer every 2,500 ms that searches the entire WPF visual tree for the grid/buttons, rewrites column binding/style and changes button `Visibility`, `Content`, and styling. This does work on the UI thread independent of actual changes. Presentation must move to native bindings/styles and one-time event hookup rather than periodic tree traversals.

### Navigation and empty-state coupling

The persistent workbench physically reparents the Expander once (correct) but `ApplyP0CommandDockState` reassigns `IsExpanded` for each selected tab and the Expanded handler invokes `RefreshControlValuesAsync`. The P1 UI hides the whole grid when `CommandSignals.Count == 0`. Because the row collection is rebuilt based on changing verification, a transient count zero looks like a true empty catalog, producing flicker. A tab switch is not a new MMS association and must never invalidate verified rows.

### Coupled diagnostic text and command authority

`SignalDefinition.ControlLastResult` calls `UpdateControlModelFromEvidence` on free-form result text; `ControlModelText` also parses status strings into the authoritative `ControlModelKind`/resolved flag. This conflates human-facing messages and protocol authority. Future correction must carry typed `ctlModel`, verified CDC, reference/FC, current association generation, and validation status in an explicit engine-derived state. Diagnostic text must never authorize or revoke an operation.

## 3. Destination architecture (do not introduce a second protocol SSOT)

One authoritative control state per (`MMS association generation`, `full normalized LD/LN.DO`). Existing ARIEC61850 typed descriptor/session stays the protocol authority; consumer only keeps a bounded **UI projection of committed proof**, with identity and generation, not a new shadow control state machine.

Model these concerns independently:

- **Membership**: operator-selected full DataObject control references; changes ONLY when selection/canonical topology changes, or actual eligibility is definitively resolved. Preserve WPF SignalDefinition / row identity for unchanged controls.
- **Proof**: `Unverified`, `Inspecting`, `VerifiedOperable`, `VerifiedStatusOnly`, `Unsupported`, `Failed`, `StaleAssociation`. Only `VerifiedOperable` with same association/typed CDC and ctlModel permits staging/Operate. `Inspecting` does not delete existing row; it disables commands if proof has been revoked, and shows inline microstatus without swapping the entire grid.
- **Flight**: `Idle`, `Claimed`, `SelectPending`, `OperatePending`, `ServiceAccepted`, `TerminationPending`, `Positive/NegativeTerminated`, `FeedbackPending`, `FeedbackConfirmed/Timeout`, `UnknownWireOutcome`. The existing runtime single-flight and quarantine authority controls this. No new concurrent write path.
- **Presentation**: labels, progress, operation stage, Interlock/Sync/Test choices and pending confirmation; these may change without altering membership.

Atomic **commit or reject** of a typed control inspection, fenced to its original association/session generation and target identity. It should not publish `ControlSemanticVerified=false` halfway through an in-place refresh of the same active association, nor let a previous session's late inspection resurrect permissions after reconnect.

### Command latency and IEDSCOUT comparison plan

Introduce monotonic stage timestamps at UI click -> runtime admitted -> initial live feedback -> SBOw send/response -> Operate send/response -> CommandTermination ingress -> final process state. Record active MMS queue time and number of competing background read requests separately. Engine PR #183 already adds local `serviceMs` and `postResponseWaitMs`, but those are NOT packet timestamps. Correlate invoke IDs and report/control timestamps with Wireshark when stage data indicates the long interval.

Display *service accepted* immediately after an actual positive MMS response, then *waiting for termination* and *process state confirmed* as independent outcomes; never show green completed until required proof. Preserve positive and negative CommandTermination, LastApplError, interlocks, sync, test, timeout/quarantine; never automatically retry uncertain physical control.

Separate background read inspection admission from control priority without parallelizing or canceling an in-flight MMS write. One association owns one command flight. Browser leaf reads (PR #636) remain operator-initiated and bounded.

## 4. Implemented in this draft

1. Replace unconditional `CommandSignals.ReplaceAll` with identity-preserving, in-place `Insert`/`RemoveAt`/`Move` reconciliation. Unchanged verified rows cause **zero collection notifications**; no `Reset` and no fresh WPF row containers. Genuine revocation removes only affected rows. Preserve existing strict `DistinctOperable` safety gate in this phase.
2. Add regression for 25 successive no-change projections, operator Interlock selection persistence, selective revoke/re-authorize, and StatusOnly/Unknown exclusion.
3. Repair a pre-existing bad test fixture introduced on the command ancestry: `P109S` tried to authorize a `SignalDefinition` with no valid CO DataObject reference, so the production guard correctly threw. Repair fixture instead of weakening guard.
4. Earlier PR #638 serializes UI dock inspection and prioritizes operator input between read admissions. Engine PR #183 measures native service stages without changing control semantics.

This is an architectural first step, **NOT** full implementation of proof-commit, removal of the 2.5-second visual timer, or staged command feedback rendering. Those require coordinated owner/lifecycle changes, tests, and physical acceptance before merge.

## 5. Next integrated phases and release gates

**Phase A — stable model/row lifecycle** (this PR): no-change zero notifications; bounded insert/remove/move; no UI row recreation, verified StatusOnly never gains control. Zero transient collection resets over a 100-switch Explorer -> Browser -> Diagnostics loop.

**Phase B — typed proof-commit and navigation decoupling**: one association-generation-aware control proof per exact DO root, atomic inspection result publication; no re-inspection on tab switch; cancel/suppress stale callbacks when association retires. Keep status-only/unsupported control disabled and explicitly represented in non-operable inventory; do not allow unverified controls into operator actions.

**Phase C — event-driven WPF**: replace 2.5-second whole-tree visual scanning and repeated preload with one-time XAML/template binding plus deliberate discovery/selection/association events. Deduplicate read requests by target and generation; no UI reparent while navigating; keep command dock fixed shell geometry and preserve user pending confirmations across ordinary tab switches (but clear on selected IED change, disconnect or unsafe lifetime boundary). No changes to the user's actual Interlock/Sync/Test settings.

**Phase D — control performance proof**: instrument engine MMS queue, SBOw/Operate service and response, termination/report arrival, feedback confirmation separately; compare same IED/same ctlModel and flags in IEDSCOUT and ARSAS. Use percentile p50/p95 for 20+ benign bench commands rather than one optimistic example. Demonstrate command admission is not blocked by discovery/report/Browser work.

**Mandatory release gates:** Windows build, full consumer/engine tests, XAML lifecycle UI tests with 100 tab transitions, command grid row/selection/scroll continuity, no reset in unchanged state, multi-IED isolation, association reconnect/stale callback rejection, slow CommandTermination, duplicate or late termination, malformed report, silent endpoint, uncertain wire quarantine, real physical IED with interlock/sync/test combinations, and exact combined-head CI after merging command #635/#638 with independent leaf #636. No public build until hardware sign-off.

**Baseline:** project-defined 60-Hz 16.7-ms frame budget and no normal >50-ms UI stalls; no unbounded queues or invisible data changes. Targets of *zero redundant MMS reads on navigation* and *zero irrelevant grid reset* are deterministic. A numeric command-latency improvement must be measured before claimed.

## 6. Cross-workstream boundaries

Command PRs #635 -> #638 -> this PR own the Command Dock and native control performance investigation; ARIEC PR #183 owns service-stage evidence. Leaf PR #636 owns SV/OR/BL Browser read, selected-only read admission and fixed IED-card progress. This model draft touches `Models/MonitorModels.cs`, which is **also modified by PR #636 in a different area**. Integration must apply both hunks intentionally on current merged head; no branch overwrite or premature release.
