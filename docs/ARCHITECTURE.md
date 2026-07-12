# ArIED 61850 Architecture

## Multi-IED ownership

```text
IED card 1 ── DeviceSession 1 ── MMS association 1 ── report/poll pipeline 1
IED card 2 ── DeviceSession 2 ── MMS association 2 ── report/poll pipeline 2
IED card N ── DeviceSession N ── MMS association N ── report/poll pipeline N
                                               │
                                               ├─ latest-value coalescing
                                               └─ global event queue
```

Connection and monitoring state are owned per IED. There is no global Start Monitor operation and stopping one IED does not stop another.

## Identity resolution

The user enters only endpoint information. After live discovery, ArIED resolves:

- explicit IEDName properties when exposed by the engine/model;
- IEDName by removing a confirmed LD instance from an MMS domain;
- a common IED prefix across multiple Logical Device domains;
- known LD-role suffixes as a final heuristic.

The application keeps these concepts separate:

```text
IEDName: OCR7SR
LDInst:  12CTRL
MMS domain / LDName: OCR7SR12CTRL
```

Exact LD metadata is preferred over heuristics. The source of the resolved identity is retained in the device model and Diagnostics.

## Selection workflow

Discovery definitions remain associated with one IED session, but the scanner grid is created only inside `SignalSelectionWizardWindow`.

- no automatic recommendation selection;
- previous user selection is restored after the real IEDName is known;
- profile lookup priority: exact IEDName+endpoint, IEDName, then endpoint;
- original ArServer IEDName profiles are imported for compatibility;
- cancel restores the pre-wizard selection;
- signal editing is disabled while that IED is actively monitoring.

## Smart acquisition order

1. Build candidates from discovered static RCB/DataSet hints.
2. Attempt static report subscription.
3. Validate actual FCD/FCDA member references.
4. For a partially covered static group, build dynamic plans for the exact uncovered remainder.
5. For points without usable static coverage, attempt a temporary dynamic DataSet/URCB.
6. Put only still-uncovered points in the MMS polling priority queue.
7. A real report update can prove and cache a vendor-specific reference alias.

A point is never removed from polling merely because it was placed in a report candidate group.

## Runtime scalability

- one monitor loop per IED, not one task/timer per signal;
- no Age or full-point stale sweep;
- `PriorityQueue` schedules only uncovered polling points;
- report lookup uses normalized/canonical reference indexes;
- report sessions are drained in bounded round-robin slices;
- latest-value callbacks are coalesced by point key;
- WPF applies value/event/diagnostic batches on a 100 ms timer;
- event and diagnostic collections are bounded;
- row/column virtualization and recycling are enabled;
- scanner UI exists only while the wizard is open.

## Time semantics

Process views contain only the timestamp supplied by the IED/report or the companion `t` attribute. Local PC receive time is not displayed in Live Values, Event Log, or CSV export.

No Local Time column is displayed anywhere in the main process or diagnostics grids. Internal diagnostic entries may still carry a runtime timestamp for ordering, but it is not exposed as an IED event timestamp.

## Dynamic reporting safety

Smart Auto enables dynamic reporting before polling. The native engine is instructed to:

- create an association-scoped temporary DataSet when possible;
- use an available URCB/RCB;
- trigger GI for the initial image;
- delete the temporary DataSet during monitor cleanup.

An IED may reject reservation, DataSet creation, RCB writes, or resource allocation. Such failures are reported explicitly and only the affected uncovered points fall back to polling.
