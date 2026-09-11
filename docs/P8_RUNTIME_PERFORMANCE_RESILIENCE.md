# P8 — Runtime Performance & Resilience

P8 hardens ARSAS for long-running, multi-IED IEC 61850 work without trading process evidence for UI smoothness.

## Non-negotiable invariants

1. **Network/process evidence is lossless.** IEC 61850 Report/GOOSE/SOE processing stays event-by-event. Only the visual latest-value projection may be coalesced.
2. **Missing data is never promoted to a valid measurement.** A missing value or unusable quality becomes `Invalid`; an absent relay timestamp stays unknown. `ReceivedAtUtc` is separate local receipt metadata.
3. **One IED cannot stall another.** Lifecycle gates, monitor cancellation, reconnect state, client instances, and report recovery are scoped per device.
4. **Native/vendor work does not execute its synchronous prefix on the WPF Dispatcher.** The UI-facing runtime facade offloads IEC 61850 lifecycle operations and provides a pre-emptive stop lane.
5. **Shutdown is bounded.** Native teardown is best effort and may not freeze the application indefinitely.
6. **Pooling is ownership-safe.** Only ARSAS-owned transient byte buffers with a strict rent/use/return lifetime may use `ArrayPool<byte>`. ARIEC61850/Npcap-owned frames and WPF-bound objects are not pooled by ARSAS.

## P8 coverage

### P8.1 — Per-IED async connection and reconnect isolation

The existing runtime already uses a per-device operation slot in the UI facade and a per-device `DeviceSession` in the monitor runtime. P8 regression tests lock these contracts so future refactors cannot accidentally introduce one global lifecycle/reconnect gate.

Reconnect replaces only the affected device client, uses bounded cleanup/connect budgets, resets only that session's report state, and re-arms reporting asynchronously after MMS association recovery.

### P8.2 — UI throttling and batching

The Engineering live workspace already separates acquisition from presentation:

- point snapshots are coalesced by point key,
- SOE entries remain in a FIFO `ConcurrentQueue`,
- diagnostics remain queued separately,
- the WPF projection flushes every 200 ms at `DispatcherPriority.Background`,
- event batches preserve every queued SOE edge.

`LatestValueUiBatcher<TKey,TValue>` is a reusable P8 primitive for additional high-rate UI surfaces. It keeps only the latest visual value per key and uses conditional removal so a newer concurrent update cannot be deleted by an older drain.

### P8.3 — Virtualized large grids

The FAT DataGrid uses row and column virtualization, recycling mode, and content scrolling. P8 adds regression coverage for these settings. Large future grids should use the same contract rather than rendering all rows/cells.

### P8.4 — Memory/leak prevention and bounded disposal

Main-window timers are stopped during shutdown; the application CTS is cancelled; GOOSE and IEC 61850 runtimes are asynchronously disposed behind bounded shutdown waits. The facade cancels every active per-device operation before disposing the inner runtime.

### P8.5 — Defensive telemetry envelope

`Iec61850TelemetryEnvelope` normalizes nullable network read output. It keeps three distinct concepts:

- process value,
- source/relay timestamp (`SourceTimestampUtc`),
- local receipt time (`ReceivedAtUtc`).

A missing process value is `Invalid` even if a malformed upstream object claims `Good`. A malformed source timestamp remains `null`; ARSAS does not fabricate relay evidence from the PC clock.

### P8.6 — Allocation profiling

`RuntimeAllocationSnapshot` captures total allocated bytes, managed heap size, fragmentation, and generation collection counters without forcing GC. It provides deltas for repeatable field/performance baselines.

### P8.7 — Ownership-safe buffer pooling

`PooledByteBufferLease` provides an idempotent `ArrayPool<byte>` lease for ARSAS-owned transient codec/network buffers. It is intentionally not applied blindly to report snapshots, signal models, GOOSE frame objects, WPF ViewModels, or buffers owned by ARIEC61850/Npcap.

Before converting an additional hot path to pooling, capture a P8.6 allocation baseline and prove that ARSAS owns the complete buffer lifetime. This avoids use-after-return corruption in protection/control evidence.

## Regression tests

P8 adds tests for:

- null/malformed telemetry normalization,
- no fabricated relay timestamp,
- latest-value UI coalescing semantics,
- independent UI keys,
- pooled-buffer lifetime and idempotent disposal,
- allocation-snapshot deltas,
- per-IED runtime/reconnect isolation source contracts,
- lossless SOE queue versus coalesced point projection,
- FAT grid virtualization,
- bounded shutdown/disposal contracts.
