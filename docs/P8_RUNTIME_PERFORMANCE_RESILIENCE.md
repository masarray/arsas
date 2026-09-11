# P8 — Runtime Performance & Resilience

P8 hardens ARSAS for long-running, multi-IED IEC 61850 work without trading process evidence for UI smoothness. The final P8 scope deliberately prefers regression-locking the production runtime that is already proven over introducing parallel helper frameworks that are not yet used by the application.

## Non-negotiable invariants

1. **Network/process evidence is lossless.** IEC 61850 Report/GOOSE/SOE processing stays event-by-event. Only the visual latest-value projection may be coalesced.
2. **Missing or unproven data is never promoted to Good evidence.** A missing process value is `Invalid`; unknown/vendor quality remains `Questionable` or `Invalid`, never silently `Good`; an absent, malformed, or incomplete relay timestamp stays unknown. `ReceivedAtUtc` is separate local receipt metadata.
3. **One IED cannot stall another.** Lifecycle gates, monitor cancellation, reconnect state, client instances, and report recovery are scoped per device.
4. **Native/vendor work does not execute its synchronous prefix on the WPF Dispatcher.** The UI-facing runtime facade offloads IEC 61850 lifecycle operations and provides a pre-emptive stop lane.
5. **Shutdown is bounded.** Native teardown is best effort and may not freeze the application indefinitely.
6. **Pooling requires measured proof and clear ownership.** ARSAS must not introduce `ArrayPool`/object pooling into ARIEC61850, Npcap, Report, GOOSE, SOE, evidence, or WPF-bound lifetimes merely because pooling is theoretically faster.

## P8 coverage

### P8.1 — Per-IED async connection and reconnect isolation

The production runtime uses a per-device operation slot in the UI facade and a per-device `DeviceSession` in the monitor runtime. P8 regression tests lock these contracts so future refactors cannot accidentally introduce one global lifecycle/reconnect gate.

Reconnect replaces only the affected device client, uses bounded cleanup/connect budgets, resets only that session's report state, and re-arms reporting asynchronously after MMS association recovery. Existing FAT multi-IED regressions also prove that one IED can be in a preparing/connected state without overwriting another IED's proven binding or session state.

### P8.2 — UI throttling and batching

The production Engineering workspace already contains the correct split between acquisition and presentation:

- point snapshots are coalesced by point key in a `ConcurrentDictionary`,
- SOE entries remain in a FIFO `ConcurrentQueue`,
- diagnostics remain queued separately,
- the WPF projection flushes every 200 ms at `DispatcherPriority.Background`,
- event batches preserve every queued SOE edge.

P8 therefore does **not** insert a second batching framework into the live path. The audit initially prototyped a generic latest-value batcher, but it was removed before P8 closure because it was unused and would have created a second presentation authority. Regression tests now protect the actual production batching path instead.

### P8.3 — Virtualized large grids

The FAT DataGrid uses row and column virtualization, recycling mode, and content scrolling. P8 adds regression coverage for these settings. Existing RCB virtualization regressions remain untouched and continue to protect recycled selection behavior.

### P8.4 — Memory/leak prevention and bounded disposal

Main-window timers are stopped during shutdown; the application CTS is cancelled; GOOSE and IEC 61850 runtimes are asynchronously disposed behind bounded shutdown waits. The facade cancels every active per-device operation before disposing the inner runtime. P8 locks these ownership boundaries without adding another global lifetime manager.

### P8.5 — Defensive telemetry envelope

`Iec61850TelemetryEnvelope` normalizes nullable network read output. It keeps three distinct concepts:

- process value,
- source/relay timestamp (`SourceTimestampUtc`),
- local receipt time (`ReceivedAtUtc`).

A missing process value is `Invalid` even if malformed upstream metadata claims `Good`. Only an explicit IEC quality of `Good` makes `IsValid` true. `Questionable` data remains separately identifiable through `IsUsable` and is never promoted to Good evidence.

Relay timestamp parsing is deliberately strict. ARSAS accepts only complete ARIEC/ISO date-time shapes containing year, month, day, hour, minute, and second. Partial strings such as `10:00:31` are rejected because general date parsers can silently fill the missing date from the local PC. A zone-less decoded IEC `UtcTime` is interpreted as UTC by protocol semantics; explicit offsets are normalized to UTC. A malformed or incomplete source timestamp remains `null` and is never replaced by `ReceivedAtUtc` or local PC time.

`Iec61850ProductionTelemetryNormalizer` is wired into the actual production discovery, cyclic MMS validation, and final report-to-runtime projection paths. Normalization therefore occurs before `RuntimePointState`, point snapshots, and SOE metadata are updated. Missing q/t metadata is not inherited from an older sample as if it belonged to the current read; a failed companion read leaves quality `Unknown` and source time `-` rather than carrying forward stale `Good` or stale relay time.

### P8.6 — Allocation profiling

`RuntimeAllocationSnapshot` captures:

- total allocated bytes,
- a current managed-memory estimate from `GC.GetTotalMemory(false)`,
- last-GC heap size and fragmentation explicitly labeled as last-GC measurements,
- generation collection counters,
- a UTC capture timestamp for diagnostics,
- an independent monotonic `Stopwatch` timestamp for rate calculations.

Allocation-rate elapsed time is calculated only from the monotonic clock, so NTP correction or a manual Windows clock change cannot corrupt `AllocatedMegabytesPerSecond`. Snapshot capture never forces GC. P8 intentionally avoids hard-coded allocation or timing thresholds in CI because runner scheduling and GC timing would make such tests flaky; thresholds should be established from measured field baselines.

### P8.7 — Pooling decision after profiling audit

No new production pooling is introduced in P8.

The audit found no measured ARSAS-owned transient-buffer hotspot that justified changing lifetime semantics. A preliminary `ArrayPool<byte>` lease was therefore removed before P8 closure. This is intentional: pooling ARIEC61850/Npcap-owned frames, Report/GOOSE/SOE snapshots, FAT evidence, or WPF-bound objects without a proven ownership boundary can create use-after-return corruption that is worse than the allocation being optimized.

If P8.6 profiling later identifies a real hotspot, pooling may be introduced in a separate measured change only after the full rent/use/return lifetime is proven to be owned by ARSAS.

## Regression coverage

P8 protects:

- null/malformed telemetry normalization,
- no fabricated relay timestamp from partial date/time input,
- explicit Good versus Questionable/Invalid quality semantics,
- production latest-value UI coalescing while SOE remains lossless FIFO,
- per-IED operation/reconnect isolation,
- existing independent multi-IED FAT state,
- FAT row/column recycling virtualization,
- bounded cancellation and shutdown/disposal ownership,
- monotonic allocation-rate timing,
- current managed-memory versus last-GC heap semantics,
- allocation snapshots that do not force GC,
- the rule that ambiguous native/process-bus ownership paths do not receive speculative pooling.
