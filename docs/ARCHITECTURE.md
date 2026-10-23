# ARSAS Architecture

ARSAS is the Windows application layer for project setup, IED session management, signal selection, monitoring, events, GOOSE and Sampled Values presentation, diagnostics, file workflows, SCL output, and guarded command presentation. IEC 61850 protocol implementation remains in the separately maintained ARIEC61850 engine.

## Layer boundary

```text
┌──────────────────────────────────────────────────────────────────────┐
│                                ARSAS                                 │
│ Explorer · Monitor · SOE · GOOSE · SMV · Files · SCL · Control · UX│
└──────────────────────────────────┬───────────────────────────────────┘
                                   │ typed application services
┌──────────────────────────────────▼───────────────────────────────────┐
│                             ARIEC61850                               │
│ MMS · Reporting · GOOSE · SMV · Files · SCL · Control · Diagnostics│
└────────────────────────┬──────────────────────┬──────────────────────┘
                         │ TCP/102              │ raw Ethernet/Npcap
                    Approved IEDs          Approved capture network
```

The application must not duplicate engine protocol state machines, BER/MMS encoding, GOOSE or SV parsing, reporting logic, file-service sequencing, SCL schema behavior, or control sequencing inside UI code.

## Multi-IED ownership

```text
IED card 1 ── DeviceSession 1 ── MMS association 1 ── report/poll pipeline 1
IED card 2 ── DeviceSession 2 ── MMS association 2 ── report/poll pipeline 2
IED card N ── DeviceSession N ── MMS association N ── report/poll pipeline N
                                                │
                                                ├─ latest-value coalescing
                                                └─ bounded UI/event dispatch
```

Connection and monitoring state are owned per IED. Stopping one IED does not stop another, and a failure on one device remains diagnosable without destroying the rest of the project workspace.

Each device session owns:

- configured endpoint and SCL context;
- connection and association state;
- discovered or cached model;
- selected signals and control objects;
- report subscriptions and dynamic-report resources;
- an uncovered polling queue only when the selected acquisition mode permits fallback;
- event, diagnostic, file-transfer, GOOSE/SMV entry-point, and command evidence;
- reconnect and cleanup lifecycle.

## Configured context versus live evidence

An SCL file describes intended engineering configuration. A live MMS association and observed process-bus traffic describe what the connected system currently exposes. ARSAS keeps these concepts distinct so comparison workflows can report meaningful design-to-live differences.

```text
Configured SCL context
        ↓
Configured IED / AccessPoint / endpoint / process-bus stream
        ↓
Live TCP/MMS association or approved passive Ethernet capture
        ↓
Observed model, values, reports, GOOSE, SV, files, and control evidence
        ↓
Design-to-live findings and attributable engineering output
```

SCL import must not silently replace live discovery when a live operation is requested. Conversely, opening an SCL file must not require an online IED merely to review configured endpoints and project context.

## Identity resolution

The user may begin with only endpoint information. After live discovery, ARSAS resolves identity using the strongest available evidence:

- explicit IED name metadata exposed by the engine or model;
- confirmed Logical Device boundaries;
- common prefixes across multiple MMS domains;
- bounded heuristics only when stronger metadata is absent.

Example:

```text
IEDName:               IED_A
LDInst:                CTRL
MMS domain / LDName:   IED_ACTRL
```

Exact metadata is preferred over heuristics. The source of the resolved identity remains available to Diagnostics.

For passive process-bus evidence, stream identity is immutable for one capture window. ARSAS records the selected APPID, destination MAC, stream ID, DataSet reference, and control reference and refuses to attach a completed snapshot to another operator selection.

## Signal-selection workflow

Discovery definitions remain associated with one device session, while the large scanner grid is instantiated only inside `SignalSelectionWizardWindow`.

- no automatic selection without user review;
- previous user selections are restored after identity resolution;
- profile lookup prefers exact identity plus endpoint, then identity, then endpoint;
- cancel restores the pre-wizard selection;
- signal editing is disabled while that IED is actively monitoring;
- row and column virtualization remain enabled for large models.

## Acquisition modes and static DataSet authority

The monitoring acquisition policy is explicit per device. A configured Static DataSet selection is **not** a synonym for adaptive report-first acquisition, and neither Discovery nor opened SCL may silently switch its selected mode.

### Static DataSet Report Only

- The canonical opened/discovered model and its ordered DataSet membership define the operator-visible signal inventory. The configured RCB-to-DataSet binding is authoritative.
- Verify the live RCB instance, exact DataSet directory and member order before enabling the report receiver, `RptEna` and GI. Do not substitute an unrelated same-DataSet RCB or guess a positional mapping.
- Use actual `InformationReport` traffic for process values. Disable cyclic MMS process polling and dynamic DataSet writes in this mode.
- If report coverage is missing, occupied, or inconsistent, show explicit unavailable/diagnostic state rather than silently falling back to polling.
- Discovery-built and opened-SCL models must converge on this same downstream authority once the canonical model is available. Preserve this mode when the FAT workspace consumes the shared session.

The accepted v1.6.40 installed-release field record documents 58/58 selected static members report-backed, with no cyclic MMS process polling: [field acceptance](V1-6-40_INSTALLED_RELEASE_FIELD_ACCEPTANCE.md). Those counts describe the tested device, not a hardcoded generic target.

### Adaptive or hybrid monitoring

Outside Static DataSet Report Only, the separate adaptive acquisition planner may:

1. Build candidates from discovered RCB and DataSet evidence and validate operational coverage.
2. Plan dynamic coverage for the exact uncovered remainder when explicitly permitted.
3. Schedule only legitimately uncovered process points for bounded MMS polling.
4. Reduce fallback only after coverage becomes operationally usable or observed.
5. Clean up association-scoped report and temporary DataSet resources on shutdown.

Hybrid fallback must never be enabled implicitly for a device held in Static DataSet Report Only mode.

## Passive process-bus workflows

GOOSE and Sampled Values capture are receive-only application workflows over engine-owned Npcap transport and decoders.

- the operator selects an approved adapter or mirror port;
- ARSAS does not transmit a GOOSE or SV frame from subscriber/snapshot workflows;
- protocol identity and continuity evidence remain separate from semantic mapping and engineering scaling;
- a publisher restart, gap, duplicate, or out-of-order sample transition prevents a clean SV continuity proof;
- changing stream selection cancels or rejects the in-flight evidence window;
- raw lanes remain unscaled until trusted SCL and reviewed measurement context are bound.

## Runtime scalability

- one monitor loop per IED, not one timer or task per signal;
- outside Static DataSet Report Only, a `PriorityQueue` schedules only uncovered polling points;
- report sessions are drained in bounded round-robin slices;
- report lookup uses normalized and canonical reference indexes;
- latest-value callbacks are coalesced by point key;
- WPF applies value, event, and diagnostic batches on a shared timer;
- event and diagnostic collections are bounded;
- row and column virtualization and recycling are enabled;
- the large signal scanner exists only while the selection window is open;
- passive SV snapshot rendering is bounded and does not run as a permanent per-sample UI pipeline.

## Timestamp semantics

Process views use the timestamp supplied by the IED, report, or companion timestamp attribute. Local PC receive time is not presented as the IED process timestamp in Live Monitor, Event Log, or CSV export.

Internal diagnostics and passive Ethernet evidence may retain runtime ordering or capture time, but those values must remain distinguishable from IED or process timestamps.

## Control-session ownership

Control descriptors and control-object sessions are scoped to the live IED association that produced them. Reconnect, association loss, model change, or cleanup invalidates state that can no longer be trusted.

The application presents semantic actions, while ARIEC61850 owns:

- `ctlModel` discovery;
- `Oper`, `SBOw`, and optional `Cancel` type resolution;
- typed `ctlVal` binding;
- Direct and Select-Before-Operate sequence execution;
- origin, control number, timestamp, Test, and Check consistency;
- CommandTermination and application-error decoding.

## Version and engine provenance

ARSAS development version metadata is kept in `Directory.Build.props`, mirrored in `VERSION` and the application project, and verified by CI. The exact ARIEC61850 integration revision is stored in `engines/ARIEC61850.lock.json` and checked out detached for tests and packaging.

Build or package success against one pinned engine revision does not imply compatibility with another unreviewed revision.

## Operational boundary

Architecture and readiness checks can establish software and protocol evidence for the tested condition. They do not establish switching authority, equipment isolation, cybersecurity approval, functional safety, calibrated measurement accuracy, universal interoperability, or formal IEC 61850 conformance.
