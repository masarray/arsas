# ArIED 61850 Architecture

ArIED is the Windows application layer for project setup, IED session management, signal selection, monitoring, events, diagnostics, and command presentation. IEC 61850 protocol implementation remains in the separately maintained ARIEC61850 engine.

## Layer boundary

```text
┌──────────────────────────────────────────────────────────────┐
│                         ArIED 61850                          │
│ Explorer · Live Monitor · Event Log · Diagnostics · Control │
└──────────────────────────────┬───────────────────────────────┘
                               │ typed application services
┌──────────────────────────────▼───────────────────────────────┐
│                         ARIEC61850                           │
│ MMS · Reporting · SCL Workspace · Control · Diagnostics     │
└──────────────────────────────┬───────────────────────────────┘
                               │ IEC 61850 / TCP 102
                         Laboratory IEDs
```

The application must not duplicate engine protocol state machines, BER/MMS encoding, reporting logic, or control sequencing inside UI code.

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
- uncovered polling queue;
- event, diagnostic, and command evidence;
- reconnect and cleanup lifecycle.

## Configured context versus live evidence

An SCL file describes intended engineering configuration. A live MMS association describes what the connected server currently exposes. ArIED keeps both concepts distinct so future comparison workflows can report meaningful design-to-live differences.

```text
Configured SCL context
        ↓
Configured IED / AccessPoint / endpoint
        ↓
Live TCP and MMS association
        ↓
Discovered Logical Devices, Logical Nodes, data, DataSets, and RCBs
        ↓
Design-to-live findings and runtime evidence
```

SCL import must not silently replace live discovery when a live operation is requested. Conversely, opening an SCL file should not require an online IED merely to review configured endpoints and project context.

## Identity resolution

The user may begin with only endpoint information. After live discovery, ArIED resolves identity using the strongest available evidence:

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

## Signal-selection workflow

Discovery definitions remain associated with one device session, while the large scanner grid is instantiated only inside `SignalSelectionWizardWindow`.

- no automatic selection without user review;
- previous user selections are restored after identity resolution;
- profile lookup prefers exact identity plus endpoint, then identity, then endpoint;
- cancel restores the pre-wizard selection;
- signal editing is disabled while that IED is actively monitoring;
- row and column virtualization remain enabled for large models.

## Report-first acquisition

1. Build candidates from discovered static RCB and DataSet evidence.
2. Attempt configured static report coverage.
3. Validate actual FCD/FCDA member references and member order.
4. For partial groups, build dynamic plans for the exact uncovered remainder where the IED permits it.
5. For points without usable report coverage, place only the remaining points in the MMS polling priority queue.
6. Allow real updates to prove reference aliases or coverage evidence.
7. Clean up association-scoped temporary DataSets and report state during monitor shutdown.

A point is not removed from polling merely because it was placed into a report candidate. Coverage must be operationally usable or observed before fallback work is reduced.

## Runtime scalability

- one monitor loop per IED, not one timer or task per signal;
- a `PriorityQueue` schedules only uncovered polling points;
- report sessions are drained in bounded round-robin slices;
- report lookup uses normalized and canonical reference indexes;
- latest-value callbacks are coalesced by point key;
- WPF applies value, event, and diagnostic batches on a shared timer;
- event and diagnostic collections are bounded;
- row and column virtualization and recycling are enabled;
- the large signal scanner exists only while the selection window is open.

## Timestamp semantics

Process views use the timestamp supplied by the IED, report, or companion timestamp attribute. Local PC receive time is not presented as the IED process timestamp in Live Monitor, Event Log, or CSV export.

Internal diagnostics may retain runtime ordering time, but that value must remain distinguishable from IED event time.

## Control-session ownership

Control descriptors and control-object sessions are scoped to the live IED association that produced them. Reconnect, association loss, model change, or cleanup invalidates state that can no longer be trusted.

The application presents semantic actions, while ARIEC61850 owns:

- `ctlModel` discovery;
- `Oper`, `SBOw`, and optional `Cancel` type resolution;
- typed `ctlVal` binding;
- Direct and Select-Before-Operate sequence execution;
- origin, control number, timestamp, Test, and Check consistency;
- CommandTermination and application-error decoding.

## Operational boundary

Architecture and readiness checks can establish software and protocol evidence for the tested condition. They do not establish switching authority, equipment isolation, cybersecurity approval, functional safety, or formal IEC 61850 conformance.
