# ARSAS Engineering Guardrails

These instructions apply to every code change in this repository unless a narrower directory-level `AGENTS.md` explicitly strengthens them. They are architecture rules, not optional style advice.

## 1. Production-ready from the first implementation

Do not land a deliberately naive, temporary, or "simple first" implementation on a production path. The first committed design must already account for realistic data volume, slow devices, partial data, cancellation, concurrent UI activity, malformed input, and long-running sessions.

Prefer bounded algorithms, deterministic state transitions, immutable/cached presentation data, and explicit failure states. Do not accept known lag, crash, unbounded memory growth, or correctness debt with the intention of fixing it later.

## 2. Defensive programming and fail-safe behavior

Treat all external, file, native, network, device, user, and persisted data as untrusted until validated.

- Validate ranges, lengths, indexes, enum values, timestamps, units, counts, and finite floating-point values before use.
- Never assume arrays from different sources have identical lengths; use validated common bounds.
- Clamp UI coordinates and time/frame requests to known valid domains.
- Bound collections and work queues. A corrupted input must not cause an unbounded allocation or loop.
- A failed optional feature must degrade locally and must not crash the workstation or corrupt shared state.
- Cleanup must be idempotent. Cancellation, close, disconnect, or repeated teardown must be safe.

## 3. Exception-free critical data paths and Result/Try patterns

Exceptions are not control flow for high-frequency or critical processing.

For parsers, conversion, analysis requests, native result decoding, timeline calculations, measurement processing, persistence validation, and other critical data functions:

- Prefer `Try...` APIs, result structs/records, status enums, or explicit success/failure objects.
- Return failure details as data instead of throwing for expected malformed input, unavailable data, unsupported capability, cancellation-aware rejection, or validation failure.
- Do not add `throw` to hot paths to represent an expected runtime state.
- Do not use broad `catch (Exception)` as the normal boundary for routine processing. If an exception boundary is unavoidable at an OS/native/framework edge, contain it at that boundary, translate it immediately to a structured result, enqueue diagnostics asynchronously, and keep exception objects out of render/update loops.
- Cancellation is a state, not an error. Prefer early cancellation checks and cancelled result states where this can avoid exception churn. Framework APIs that necessarily throw `OperationCanceledException` may be contained at the task boundary only.

A practical result shape should carry at least success/state, stable error code, concise operator-safe message, and optional diagnostic context without requiring exception propagation.

## 4. Asynchronous internal diagnostics

Failures that matter to engineering diagnosis must not burden interactive UI paths.

- Route diagnostics through a bounded, asynchronous internal diagnostic queue or existing equivalent diagnostic service.
- UI/status text receives only concise operator-facing state; detailed context goes to diagnostics.
- Never synchronously write large logs, serialize reports, or perform filesystem/network diagnostics from cursor, render, packet, measurement, or polling callbacks.
- Apply backpressure/coalescing/deduplication to repeated diagnostic events.
- Diagnostics must never become a new crash source; queue failure is non-fatal.

## 5. Hot-path performance rules

The following are hot paths when active: rendering, cursor/scrub movement, mouse move, waveform navigation, live value updates, protocol receive callbacks, measurement loops, polling, packet processing, and device state projection.

On hot paths:

- No blocking I/O.
- No synchronous waits on async work (`.Wait()`, `.Result`, busy waiting).
- Avoid LINQ and iterator pipelines when they allocate or repeat enumeration per frame/update.
- Avoid rebuilding dictionaries, lists, formatted text, geometries, brushes, pens, or large strings every frame when data is unchanged.
- Cache immutable/static drawing layers separately from rapidly changing overlays.
- Freeze WPF `Freezable` objects when safe and reuse them.
- Coalesce high-frequency updates to the presentation cadence and enforce latest-wins semantics.
- Allow at most the explicitly designed number of in-flight workers. Never create one background task per mouse move, packet, or value change.
- Use O(1) or O(log n) lookup on repeated interactive searches where practical; pre-index sorted snap/event data rather than linearly scanning on every cursor move.
- Long-record visualization must be bounded/decimated while preserving exact source identity for drill-down.

## 6. UI responsiveness and thread ownership

The WPF dispatcher owns UI objects only. Native/file/network/CPU-heavy work belongs off the UI thread.

- Keep dispatcher work small and presentation-only.
- Do not perform native scans, file reads, DFT/harmonic calculations, large parsing, or report generation on the UI thread.
- Background results must be generation/revision checked before presentation so stale results cannot overwrite newer user intent.
- Mode switches, close/dispose, and new requests must invalidate or cancel obsolete work.
- Pointer feedback must remain synchronous and lightweight even if deeper analysis is still computing.

## 7. Deterministic state and stable ordering

Never let `HashSet`, dictionary enumeration, task completion order, or checkbox activation order accidentally define operator-visible ordering.

- Define canonical ordering for operator-visible collections.
- Preserve relative order inside semantic categories unless a documented sort key applies.
- One concept has one authority: cursor identity, timebase, selection, connection ownership, measurement state, and device state must not have competing sources of truth.
- Derived UI must project from authoritative state rather than maintaining an independent shadow state.

For COMTRADE Time Signals specifically: selected analog tracks are always rendered before selected digital/protection tracks; selection order must not alter that category ordering.

## 8. Memory, allocation, and resource bounds

Every long-lived cache, queue, history, evidence buffer, waveform data set, and diagnostic buffer must have a documented bound or lifecycle.

- Prefer fixed-size/ring/LRU-style bounded caches where retention is useful.
- Release obsolete cancellation tokens, event subscriptions, native handles, timers, streams, and large buffers promptly.
- Avoid copying large arrays unless the copy provides a measured or correctness benefit.
- Reuse immutable data between views when authority and lifetime are clear.
- Never retain UI objects in global/static caches.

## 9. Native and protocol boundaries

Native ArdIrec/ARIEC61850 and protocol integrations are authoritative engineering boundaries.

- Validate native capability before use.
- Check native return/status values before reading output buffers.
- Do not duplicate authoritative native calculations in managed UI code merely for convenience.
- Keep native calls outside render callbacks.
- Serialize access only where the native contract requires it; do not use a broad lock/gate that unnecessarily blocks unrelated UI work.
- Preserve raw/source frame identity through decimation and projection so engineering evidence remains traceable.

## 10. Regression-proof changes

Every bug fix or architecture rule that can regress must gain an automated contract where practical.

At minimum, tests should cover:

- the field-reported failure mode;
- boundary and malformed input behavior;
- cancellation/stale-result behavior where asynchronous work is involved;
- deterministic ordering/identity rules;
- large/bounded data behavior for algorithms designed to protect responsiveness.

A green build alone is not enough for a performance claim. Add deterministic allocation/algorithmic contracts and, where stable in CI, benchmark or latency budgets. Do not add flaky wall-clock tests that depend on shared-runner speed.

## 11. Performance acceptance

For interactive workstation features, reason about and document four budgets:

1. work per pointer/render/update event;
2. maximum in-flight asynchronous work;
3. maximum retained memory/cache size;
4. stale-work invalidation/cancellation behavior.

Prefer algorithmic tests for these budgets. Use field/benchmark evidence for actual latency numbers; never claim a specific p95/p99 latency without measurement.

## 12. Change discipline

Before changing architecture:

- inspect existing authority, lifecycle, tests, and hot-path design;
- preserve working field behavior unless the change intentionally corrects it;
- modify the smallest authoritative layer rather than adding another parallel mechanism;
- avoid copy-pasted alternative implementations;
- keep commits focused and diagnosable;
- do not merge a field-test PR until exact-head CI is green and the requested field acceptance has been received.

## 13. COMTRADE workstation invariants

For the COMTRADE workspace, preserve all of the following unless a newer accepted specification explicitly replaces them:

- a shared corrected timebase and trigger origin;
- one authoritative snap index for visible digital edges;
- C1/C2 waveform and upper ruler represent the same cursor identities;
- one P cursor for Phasor and one H cursor for Harmonics;
- interactive cursor feedback must not rebuild waveform data geometry;
- Phasor/Harmonics analysis is coalesced and stale results cannot flash back after a newer/final request;
- large records use bounded overview data plus exact source-frame drill-down;
- accented/legacy CFG labels degrade safely instead of corrupting the workstation;
- Harmonics uses native ArdIrec analysis and presents checked analog channels consistently;
- selected analog tracks render above all selected digital tracks.

When a requested change conflicts with one of these invariants, redesign the authority intentionally; do not patch around it with another shadow state.
