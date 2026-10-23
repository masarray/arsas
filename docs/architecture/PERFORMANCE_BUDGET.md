# ARSAS Performance Budget

This document turns performance into a regression contract. Values below are engineering guardrails for supported/reference test environments, not universal guarantees across all hardware.

When an absolute target is not yet baselined, the current qualified main-branch measurement is the baseline. A performance-sensitive PR must measure the same scenario before and after the change.

## 1. UI/frame budget

- 60 Hz frame duration: 16.7 ms total.
- No individual synchronous application operation should routinely consume the full frame budget.
- Cursor, hover, selection, and drag visual feedback should remain frame-local/lightweight; expensive engineering recomputation must be coalesced/backgrounded.
- New code must not introduce sustained UI stalls > 50 ms during normal interaction.
- Performance-sensitive UI changes should not worsen p95 interaction/frame latency by more than 10% versus the qualified baseline without explicit justification.

## 2. High-frequency event budget

- Acquisition/event frequency is independent from UI refresh frequency.
- No unbounded queue is permitted.
- Telemetry/log/visual updates that do not require lossless intermediate state should be coalesced or sampled to a bounded presentation cadence.
- Queue depth, drop/coalesce counters, or another overload indicator must be observable for newly introduced sustained producer/consumer pipelines.

## 3. Large-file/data budget

- Working-set growth must remain bounded and must not require duplicate whole-record copies as the normal architecture.
- File size must not imply equal-size duplication across parser model + per-channel model + renderer model without measured justification.
- Dense waveform geometry should scale with viewport width/visible information rather than full sample count.
- Large-file parsing/open changes should not regress qualified throughput or peak working set by more than 10% without documented reason and evidence.

## 4. Cursor/analysis budget

- Moving a cursor must not trigger a full-file parse, full waveform rebuild, full locus rebuild, or full harmonic/FFT pipeline unless the operation inherently requires it and is backgrounded.
- Static plot layers and dynamic overlays remain separate.
- Final cursor value after drag/coalescing must be exact even if intermediate expensive computations are sampled.

## 5. Allocation and memory budget

- Hot paths should avoid repeated large allocation/copy churn.
- New pooling/caching is allowed only with a defined ownership/lifetime model and measured benefit.
- For an existing benchmark/workflow, peak working set and allocation volume should not regress >10% without explicit review.
- Resource count must return to a stable level after repeated open/close/reload cycles; monotonic growth is treated as a leak until disproven.

## 6. Protocol/device processing budget

- Receive/device callbacks do only bounded decode/state publication work and never perform expensive UI rendering directly.
- Retry/reconnect loops are bounded and include backoff/timeout/cancellation semantics.
- Diagnostic/logging load cannot reduce protocol correctness or cause an unbounded queue.
- Error storms must aggregate/deduplicate/rate-limit rather than scale log/UI work linearly with every repeated failure.

## 7. Diagnostics budget

- Critical/hot producers enqueue only compact structured diagnostic data.
- Diagnostic queues/channels are bounded and non-blocking to critical work.
- Formatting, persistence, export, and UI formatting occur off hot paths.
- Queue saturation has an explicit drop/coalesce policy and counters.

## 8. Startup and packaged-app budget

- Build success is not startup evidence.
- Packaged startup/smoke tests remain required when packaging/runtime wiring changes.
- Startup-to-interactive time should not regress >10% against the qualified baseline without documented benefit.
- Non-critical initialization should be deferred until the interactive shell is ready when safe.

## 9. Benchmark protocol

For a performance-sensitive PR record:

```text
Scenario:
Base commit:
Candidate commit:
Machine/OS:
Build configuration:
Dataset/device fixture:
Iterations/warm-up:

Metric                  Base        Candidate     Delta
------------------------------------------------------
startup                 ...         ...           ...
operation latency p50   ...         ...           ...
operation latency p95   ...         ...           ...
peak working set        ...         ...           ...
allocation volume       ...         ...           ...
queue max depth         ...         ...           ...
CPU                      ...         ...           ...
```

Do not compare Debug versus Release or different datasets/machines and call the result an optimization.

## 10. Regression rule

A >10% regression in a relevant measured performance metric is not automatically forbidden, but it must be visible, explained, and justified by a correctness/capability trade-off. Hidden regressions are not acceptable.

Correctness and engineering truth remain higher priority than benchmark numbers.