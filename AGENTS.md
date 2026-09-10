# AGENTS.md — ARSAS Production Engineering Contract

These rules apply to every AI/code agent working in this repository. ARSAS is professional substation-engineering software; engineering correctness, deterministic behavior, responsiveness, field robustness, and regression safety are product requirements from the first implementation.

## 1. Prime directive

Do not begin with a deliberately naive, disposable, prototype-only, or intentionally simplified implementation when the production architecture is already knowable.

Design the smallest production-quality solution that satisfies the requirement without unnecessary architectural complexity.

Priorities, in order:
1. engineering correctness and data integrity;
2. failure containment and crash resistance;
3. regression compatibility;
4. UI responsiveness and bounded latency;
5. performance and bounded memory;
6. maintainability and testability.

Do not sacrifice existing working capability to make a new screenshot or demo pass.

## 2. Mandatory workflow before editing

For non-trivial bugs, features, performance work, or architectural changes, follow this sequence:

RECONNAISSANCE -> REPRODUCE/BASELINE -> ROOT CAUSE -> INVARIANTS -> ARCHITECTURE IMPACT -> IMPLEMENT -> REGRESSION TEST -> FAILURE-PATH TEST -> PERFORMANCE CHECK -> CI/BUILD -> USER WORKFLOW VALIDATION

Before changing code:
- locate the current implementation and all known consumers;
- identify the authoritative state/model and avoid creating a second source of truth;
- identify related tests, serialization formats, protocol mappings, view-model bindings, and lifecycle owners;
- state which existing behaviors must not change;
- determine root cause before applying a patch;
- prefer an existing abstraction over creating a parallel subsystem.

Do not repeatedly modify code hoping one version works. If an attempt fails, stop, re-check assumptions, gather evidence, then revise the design.

Three patches in the same subsystem for the same symptom are a signal to re-audit the root cause and architecture.

## 3. Architecture boundaries

Keep dependencies directional where practical:

Presentation / XAML / View
-> Application / orchestration / use cases
-> Domain / engineering models / calculations
-> Infrastructure / device / file / network / OS adapters

Rules:
- engineering calculations must not depend directly on UI controls;
- filesystem, network, device, database, update, export, and OS integration should remain behind explicit service/adaptor boundaries;
- avoid global mutable state;
- prefer one authoritative document/session state model;
- do not duplicate engineering data merely to simplify UI code;
- do not introduce abstractions for hypothetical future requirements without a current need.

## 4. Defensive programming and failure containment

Treat all external inputs as fallible: COMTRADE, SCL, packet captures, IEC 61850 responses, device/network traffic, local files, configuration, persisted state, user input, and update metadata.

Validate before use:
- nullability and missing fields;
- bounds, lengths, indexes, and channel counts;
- numeric ranges, overflow, NaN and Infinity;
- schema/version assumptions;
- malformed, partial, truncated, stale, or inconsistent data;
- timeouts, cancellation, disconnect, and partial completion.

For C# prefer nullable reference types, pattern matching, TryParse-style APIs, explicit guards, typed result/error models, `using`/`await using`, and cancellation tokens where appropriate.

Do not wrap every function in a broad `try/catch`. Catch at meaningful failure boundaries. Never silently swallow failures. Recover locally when safe; otherwise propagate a structured failure to the owning layer and keep the application usable when isolation is possible.

One malformed field record must not crash the entire application.

## 5. Zero UI blocking

The UI thread exists for presentation and interaction.

Never perform synchronous long-running:
- file parsing or export;
- network/device communication;
- SCL/COMTRADE bulk processing;
- FFT/harmonic/phasor/locus calculations;
- report generation;
- database or package/update work

on the UI thread.

For a 60 Hz interface, ~16.7 ms is the total frame budget, not permission for an individual operation to consume 16 ms repeatedly.

Use async I/O for I/O-bound work and background workers/tasks for CPU-bound work. User-triggered work that can outlive its screen/session must support cancellation when practical. Marshal only minimal results back to UI state.

Never use arbitrary `Task.Delay`/timers to hide a race condition.

## 6. Streaming, batching, backpressure

High-frequency streams such as waveform updates, packet/event feeds, device telemetry, logging, cursor-driven analysis, or live IEC 61850 data must not trigger one expensive UI update per incoming event.

Use bounded queues, coalescing, batching, throttling, latest-value semantics, or backpressure according to domain needs.

Rules:
- avoid unbounded queues;
- avoid one task/thread per event;
- separate acquisition frequency from presentation frequency;
- preserve all samples only when the engineering requirement is lossless;
- otherwise prefer latest-state/coalesced rendering;
- always commit the exact final interaction value after coalesced drag/update flows.

## 7. Large files and large datasets

Do not eagerly load entire large engineering files into multiple duplicate in-memory structures when streaming/indexed access is practical.

Prefer:
streaming -> chunked parse -> indexed metadata -> bounded working set -> viewport/analysis-specific access

For very large local files, consider memory mapping when it materially improves the workload and lifetime model.

Avoid full-record rescans for small cursor or viewport changes.

## 8. Waveform, chart, table, and tree virtualization

Rendering cost must scale primarily with visible information, not total dataset size.

Large lists, trees, protocol frames, event logs, tables, and engineering grids must use virtualization/lazy loading/paging where supported.

Dense waveform/time-series rendering must use viewport-aware LOD/downsampling before drawing. For disturbance waveforms, prefer extrema-preserving min/max envelope strategies over simple averaging so short transients and trip spikes are not hidden.

Keep static layers (grid, axes, protection zones, base geometry) separate from high-frequency dynamic overlays (cursor, selection, hover, live markers) to avoid full-scene invalidation.

Never regenerate a full waveform, locus, or harmonic dataset merely because a cursor moved.

## 9. Memory and resource lifetime

Avoid unnecessary allocations/copies in hot paths.

Prefer reusable buffers, retained capacity, spans/views, pooled arrays only when profiling shows allocation pressure, and precomputed indexes instead of repeated scans.

Do not add a generic object pool merely because pooling sounds faster.

Every owned resource must have an explicit lifecycle: files, streams, sockets, timers, subscriptions, event handlers, cancellation sources, device handles, unmanaged buffers, workers, and GPU resources.

Dispose/unsubscribe/release when ownership ends. A document reload/close must not leave callbacks pointing to destroyed state.

## 10. IEC 61850 / device / protocol rules

Never assume an IED, gateway, capture, or remote endpoint behaves perfectly.

Validate declared lengths before field access. Use explicit timeouts. Handle disconnect, reconnect, cancellation, negative responses, partial responses, unsupported services, malformed frames, and stale state.

Protocol state machines must have explicit transitions and bounded retry behavior. Unexpected frames must fail safely rather than corrupt session state.

Device/network callbacks must not perform expensive UI work directly.

Do not cosmetically alter protocol/engineering data to imitate another product. UI representation may be optimized, but timestamps, values, quality, sequence, trigger semantics, phasors, impedance, zones, and report facts must remain engineering-correct.

## 11. Performance as a contract

Performance-sensitive paths should define and preserve measurable budgets where practical:
- startup time;
- file-open latency;
- parsing throughput;
- interaction/cursor latency;
- UI frame time;
- allocation rate and working-set memory;
- report/export time;
- packet/event processing throughput;
- queue depth under burst load.

Do not claim an optimization without evidence. Prefer algorithmic/layout improvements over speculative micro-optimizations.

Do not add caches, worker pools, SIMD, object pooling, or complex concurrency unless the bottleneck and ownership model are understood.

## 12. Regression prevention

Every bug fix should add or update a regression test whenever technically practical.

Test the exact failure mode that motivated the change, not only nearby happy paths.

Before changing shared behavior, identify callers and persisted/public contracts. Do not change serialization, configuration, protocol mapping, default values, timing semantics, report semantics, or public APIs without compatibility analysis.

For UI bugs, protect interaction semantics in addition to appearance.

## 13. Change discipline

Prefer the smallest coherent change that fixes the root cause.

Do not:
- mix unrelated refactoring into a focused fix;
- create duplicate services/state stores because understanding the existing path is inconvenient;
- rename large areas without a compelling reason;
- add a dependency when the platform/current stack already provides the capability;
- replace a working subsystem simply because a rewrite appears easier.

A new dependency must justify purpose, maintenance cost, binary impact, security implications, and runtime overhead.

## 14. Exception-free hot paths, Result pattern, and asynchronous diagnostics

Expected or recoverable failures must not use exceptions as normal control flow in performance-critical or high-frequency code. This includes COMTRADE/SCL parsing loops, IEC 61850 frame decoding, packet/event processing, waveform/harmonic/phasor calculation loops, device acquisition callbacks, and rendering-preparation hot paths.

Prefer explicit C# failure contracts such as `TryXxx(...)`, typed `Result<T>` / result records, discriminated status models, nullable returns only when the failure meaning is unambiguous, and structured error codes. A normal timeout, malformed field, missing sample, unsupported value, disconnected device, or parse rejection should not require stack unwinding.

Exceptions from .NET, OS APIs, filesystem/network libraries, or third-party code may still occur. Catch them at the nearest meaningful infrastructure/application boundary, convert them into the repository's structured result/error model, preserve cancellation semantics, and keep exception handling out of inner loops. Do not catch and ignore exceptions.

For hot-path diagnostics, never synchronously write files, console logs, telemetry, UI dialogs, JSON, or expensive formatted strings. Publish a small structured diagnostic event to a bounded asynchronous diagnostic channel/queue and let a background consumer aggregate, format, persist, or surface it.

Diagnostic queues must be bounded and have an explicit overload policy. Deduplicate/rate-limit repeated failures and aggregate counts such as `MalformedRow x 4281` instead of enqueueing thousands of equivalent messages. A full/broken diagnostic queue must never block protocol processing, parsing, rendering, or UI responsiveness; retain counters/high-severity/latest events according to documented policy.

The diagnostic subsystem is observational, not a correctness dependency. Logging failure must not become application failure.

When implementing a `Result<T>` family, keep it lightweight and consistent. Do not create multiple incompatible result abstractions in different subsystems. Error payloads should carry stable machine-readable codes/context first; human-readable formatting belongs outside the hot path.

## 15. Definition of done

A task is not complete because it compiles.

Validate, as applicable:
BUILD
+ STATIC ANALYSIS
+ UNIT TESTS
+ REGRESSION TESTS
+ INTEGRATION/DETERMINISTIC FIXTURES
+ NEGATIVE/FAILURE-PATH TESTS
+ PERFORMANCE/ALLOCATION CHECK
+ RESOURCE/LIFECYCLE CHECK
+ PACKAGED STARTUP/SMOKE TEST
+ REAL USER WORKFLOW CHECK

Use the repository PR template and existing engineering validation gates. Never claim a check was run when it was not.

## 16. Agent completion report

After implementation, report:
- Changed: what was modified;
- Root cause: why the previous behavior failed;
- Architecture: why this solution belongs in the existing design;
- Regression protection: tests/invariants added;
- Performance impact: measured result or why the path is not performance-sensitive;
- Validation: exact checks/commands and results;
- Remaining limitations: genuine unresolved limitations only.

## Final rule

Think like the maintainer who must support ARSAS on real engineering data for years, not like a prototype generator trying to make today's screenshot pass.

Understand first. Fix root causes. Preserve working behavior. Keep hot paths bounded. Validate failure modes. Measure performance when relevant. Prevent regressions before declaring done.
