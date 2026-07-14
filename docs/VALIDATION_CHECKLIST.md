# ArIED 61850 Validation Checklist

Record the application commit, ARIEC61850 commit, Windows version, .NET version, test data provenance, and evidence source for every completed validation set.

## Build and package

- [ ] `scripts/verify-source-clean.ps1` passes.
- [ ] Restore and Release build pass against the documented ARIEC61850 integration baseline.
- [ ] Required Smart Control and SCL workspace contracts are detected by CI.
- [ ] Portable x64 package is generated with the intended version.
- [ ] Package contains the required license, notice, copyright, trademark, support, and third-party documents.
- [ ] Package does not contain source, captures, logs, local paths, confidential project data, or obsolete license files.

## SCL workspace

- [ ] SCD, CID, ICD, IID, SSD, and XML file filters open the intended test files.
- [ ] XML loading rejects external entities and prohibited DTD behavior.
- [ ] Multi-IED files produce the expected IED and AccessPoint context.
- [ ] Duplicate endpoints are not added more than once.
- [ ] An IED without an IP address remains visible as configured offline context.
- [ ] Opening SCL does not require a live network connection.
- [ ] Live connection and discovery occur only when the user requests an online action.
- [ ] Configured model context remains distinguishable from the observed live MMS model.
- [ ] Design-to-live comparison reports missing, extra, and mismatched model elements without rewriting the source SCL.

## IED identity

- [ ] The card name changes from the temporary endpoint label to the resolved IED name after discovery.
- [ ] Logical Device instances remain separate from the IED name.
- [ ] Synthetic domains such as `IED_ACTRL` resolve to the expected `IED_A` and `CTRL` split.
- [ ] Multiple MMS domains from one IED produce one stable identity.
- [ ] Diagnostics records whether identity came from explicit model metadata, LD instance removal, common prefix, or bounded heuristic fallback.
- [ ] No customer, employer, or proprietary device identifier is required by the validation fixture.

## Per-IED workflow

- [ ] Every card has independent connect, disconnect, start, stop, configure, and remove actions.
- [ ] Connecting or discovering IED B does not block monitoring of IED A.
- [ ] Stopping IED A does not change IED B.
- [ ] A failed device does not destroy the other device sessions.
- [ ] The selection wizard opens only for its owning IED.
- [ ] No signals are automatically selected on first discovery without user review.
- [ ] Cancel restores the previous selection.
- [ ] Saved selection returns for the same resolved identity.
- [ ] Saved selection returns by endpoint when identity matching is unavailable.
- [ ] Project save and open preserve the intended device and selection context.

## MMS discovery

- [ ] Logical Devices and Logical Nodes are discovered in stable order.
- [ ] Data Objects and Data Attributes retain functional constraint and type context.
- [ ] Values, quality, and IED timestamps are mapped to the correct references.
- [ ] DataSet directory order is preserved.
- [ ] RCB inventory identifies buffered and unbuffered candidates where exposed.
- [ ] Unsupported file service or optional metadata is reported without discarding an otherwise valid model.
- [ ] Reconnect creates a fresh native client and rebuilds session-scoped discovery state.

## Reporting and monitoring

- [ ] Existing static DataSet and RCB coverage is attempted first.
- [ ] Actual DataSet members map to the correct selected references.
- [ ] Member order remains consistent with report values.
- [ ] Partially uncovered static groups receive a dynamic recovery attempt where supported.
- [ ] Points without usable static coverage receive a temporary DataSet or URCB attempt before polling where supported.
- [ ] Dynamic-monitor cleanup removes temporary resources when permitted by the IED.
- [ ] Only still-uncovered or unverified points enter cyclic MMS polling.
- [ ] Report-covered points use report-primary acquisition with only the configured verification or fallback behavior.
- [ ] GI provides an initial image when supported.
- [ ] Reservation, resource, DataSet, or RCB write rejection produces a clear diagnostic.
- [ ] Acquisition identifies the actual source, such as configured report, temporary dynamic report, or polling.
- [ ] A point is not removed from polling merely because it was placed in a candidate report group.

## Process data and events

- [ ] Discovery progress affects only the busy IED and does not block the rest of the application.
- [ ] Connection, monitoring, report activity, unread event, and diagnostic states are understandable without relying only on color.
- [ ] Live Monitor contains IED timestamp but does not present local receive time as process time.
- [ ] Event Log contains semantic value transitions and does not create a process event for quality-only changes unless explicitly designed.
- [ ] Event Log and CSV retain the originating IED and signal reference.
- [ ] Breaker and switch double-point values decode correctly.
- [ ] Boolean start, operate, trip, and indication edges are correct for the synthetic model.
- [ ] Analog magnitude and unit are correct.
- [ ] Missing quality is shown as unavailable and is not promoted to Good.
- [ ] Copy and export output remains bounded and sanitizable.

## Smart Control

- [ ] The selected reference resolves to the intended control Data Object root.
- [ ] `ctlModel` is read from the live IED before dispatch is enabled.
- [ ] Direct normal, SBO normal, Direct enhanced, and SBO enhanced models follow the required sequence.
- [ ] `Oper`, `SBOw`, and optional `Cancel` types are retrieved from the live model.
- [ ] The named `ctlVal` field is located without positional guessing.
- [ ] DPC, SPC, INC/ISC, BSC, and APC values bind only when representable by the live type.
- [ ] Origin, `ctlNum`, timestamp `T`, Test, interlock, and synchrocheck values remain consistent through one sequence.
- [ ] Enhanced models wait for positive or negative CommandTermination.
- [ ] `ControlError`, `AddCause`, and `LastApplError` are surfaced.
- [ ] Open and Close require the staged confirmation action before dispatch.
- [ ] Cancel clears the staged command without dispatch.
- [ ] Selection timeout, Cancel, association loss, and competing-client ownership produce deterministic outcomes.
- [ ] Process feedback mapping is independently verified.
- [ ] No generic `.Oper`, `.SBOw`, or `.Cancel` write fallback is available.
- [ ] A descriptor that is not operationally ready leaves dispatch disabled.

## Large-model behavior

- [ ] 10,000 points do not create 10,000 tasks or timers.
- [ ] The signal scanner is not retained as a permanent main-workspace grid.
- [ ] Poll scheduler CPU remains stable when most points are report-covered.
- [ ] Repeated reports use indexed reference lookup.
- [ ] Event bursts do not create one Dispatcher operation per update.
- [ ] Latest-value updates are coalesced by point key.
- [ ] Event and diagnostic collections remain bounded.
- [ ] WPF row and column virtualization remain enabled.
- [ ] The UI remains usable at the tested Windows scaling levels.

## Recovery and cleanup

- [ ] Cable removal affects only the owning IED session.
- [ ] Reconnect creates a fresh native client and invalidates stale association-scoped control state.
- [ ] Saved signal selection remains available after disconnect and reconnect.
- [ ] No duplicate live points, report plans, or polling entries appear after restart.
- [ ] Temporary DataSets and report state are cleaned up when supported.
- [ ] Application shutdown completes without re-entering the WPF closing lifecycle.

## Claim boundary

- [ ] The evidence source is identified as static review, automated test, loopback, simulator, isolated laboratory IED, or approved commissioning environment.
- [ ] Results do not claim formal conformance, universal interoperability, cybersecurity approval, functional safety, switching authority, or equipment isolation.
- [ ] Shared evidence is synthetic or sanitized and legally redistributable.
