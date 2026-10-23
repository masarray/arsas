# ARSAS — Unified IEC 61850 Model / Reactive SSOT v2

**Status:** incremental engineering contract; phase 1 implemented on PR #695; **not released**.
**Dependency:** PR #688 (SG7 UI budgets, model preparation); includes #679 Smart IP visual startup fixes and #686 SG7 Browser Write/Command Dock. All inherited history must stay intact. **Engine lock remains pinned.**

## One authority, not one mutable UI object

- **Engineering schema**: `LiveDiscoveryModel` / verified SCL, indexed once by immutable **model identity**; exact LD/LN/DO/DA/FC, DataSet member order, RCB and q/t relationships. Source declarations are not live evidence.
- **Canonical current observation**: `Iec61850MonitorDevice.LiveValues` / `IedBrowserLiveValues` is the per-IED, association-scoped owner for admitted Report and MMS Read observations. Typed q/t evidence and source provenance are never guessed or copied from a display string.
- **Runtime acquisition**: MMS Report first, association-safe Read/poll fallback according to configured policies. Control requests/results are separate from actual status observations and cannot fabricate process truth.
- **GOOSE publisher**: a different transport and identity (publisher/stream/DataSet/ordinal). Never write into the MMS identity merely because two labels look alike. A future verified mapper can publish separate GOOSE-origin observations for a *proven* owner/ref/FC; no cross-IED aliasing.
- **SOE & command lifecycle**: chronological, lossless events. A latest-value dirty-key journal is **not** event history.

```text
MMS Report ----+
MMS Read/Poll -+--> canonical admission, exact FC + association ----+
               |                                                  |
GOOSE frames --+--> independently verified stream/leaf evidence --+--> source-aware views
                                                                  |
                    canonical revision/change journal ------------+--> independent cursors
                                                                    --> viewport subscription
                                                                    --> small UI row patches

Immutable engineering model -- shared schema index --+
                                                      +--> Browser/Explorer/FAT/Command read-only projections
```

## Implemented now — phase 1

1. `Models/SharedIedSchemaIndex.cs`: `ConditionalWeakTable` keyed by *actual source model*, one `Lazy<Task<IedBrowserSchemaIndex>>` per model. `PrepareAsync` never parses on the WPF Dispatcher. Browser and `IedBrowserReadBindingIndex` share the same instance. The existing binding constructor is synchronous for compatibility, which is a **known future migration point**; don't hide it in FAT results.
2. `Models/CanonicalValueChangeJournal.cs`: fixed ring of 4096 immutable change records. Records are **only** revision + association + exact reference/FC + channel kind; not process values, samples, or SOE.
3. `IedBrowserLiveValues.ReadChanges(afterRevision,max)`: independent non-destructive cursor access under the already existing ledger gate. A journal gap, invalid cursor, or association reset triggers `RequiresFullRefresh`. A slow consumer must resync from the canonical ledger before returning to incremental projection. Each accepted canonical publication records a contiguous version; rejected/stale network callbacks emit **nothing**.
4. Phase-1 tests validate multi-consumer cursor replay, concurrent model preparation identity, reconnect fences and overflow detection.
5. Original #688 WPF bounded scheduling remains in place, without changing selection, command, Report or SOE semantics.

## Target migration — controlled follow-up phases

**Phase 2: viewport-aware subscriptions / value-only patches**

- Build exact FC-qualified per-IED reference -> consumer/visible-row index, with explicit dependency keys for vectors, parent DO summaries, quality/timestamp, SIUnit/multiplier and RCB readback.
- When selecting an IED, paint shell immediately, then read **current** canonical values only for visible viewport (+ small overscan). Do not enumerate all SG7 attributes, rebuild all binding rows, or trigger a second MMS scan.
- Consume the per-workspace cursor in bounded batches. Only changed displayed row fields raise `PropertyChanged`. Offscreen keys mark view versions/dirty state, **not** WPF events.
- If `RequiresFullRefresh`, reconcile snapshot from the same ledger/model for that IED; preserve scroll, selection and command focus.
- Preserve separate SOE FIFO, report discontinuities, BufOvfl, q/t typed evidence and control Accepted/Termination ordering.

**Phase 3: shared projection adapter for Explorer, Live Monitor, FAT and Command Dock**

- Delete duplicated ad-hoc presentation caches only *after* proving feature parity.
- Point and SignalDefinition are UI adapters, **never independent writers of process truth**.
- Replace per-workspace full CollectionView refresh / dynamic sort with incremental filtered indexes and stable virtualization.
- Proven GOOSE model binding feeds a **separate source-aware** channel. TTL expiry is freshness state, not a fabricated MMS value change.

**Phase 4: physical FAT / promotion**

- Four concurrently monitored IEDs, including SG7_BCU (~32.9k MMS variables/~25.5k DA); 100 alternating IED switches, selected and unselected; LN/DO open/close, continuous scroll, active Report storm, GOOSE frames, browser read/write, control docks.
- ETW `ARSAS-IEDBrowser-Performance` + PerfView/WPF Dispatcher, GC allocation, backlog, time-to-first-paint and p50/p95/p99 interaction latency. Record *numbers*, not subjective `feels fast`.
- Suggested initial gates: p95 selection input feedback <=50 ms; indexed Browser first paint <=150 ms; per UI patch p95 <=4 ms; no application-caused Dispatcher block >50 ms; no stale association, fabricated quality, missing SOE/RCB/control events.
- Main and release remain gated on exact commit CI + physical FAT. Faster UI **must never** mean dropped protocol evidence or hidden Reads.

## Zero-regression guardrails

- Git branch ancestry must include latest relevant #679/#686/#688, checked at handoff and before merging; no force push.
- Engine lock must match the qualified ARIEC61850/native-bridge revisions.
- Do not reimplement MMS, RCB, FCDA identity, q/t parsing, source precedence, SOE or command sequence in a WPF adapter.
- Publication and version changes are atomic under the current canonical `_gate`. Never call user/WPF event handlers under that gate.
- Any UI scheduler may coalesce latest **presentation** per key, never chronological events.
- No feature is called complete on a passing static/source test alone: canonical evidence, source compatibility, peak memory and live multi-IED responsiveness must all be measured.

**Engineering checkpoint:** PR #695 remains draft until Windows CI and later phase-specific SG7_BCU field acceptance evidence are complete.
