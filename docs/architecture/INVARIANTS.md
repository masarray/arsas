# ARSAS Architecture and Product Invariants

These invariants are constraints, not implementation suggestions. A change that violates one requires an explicit architecture decision, compatibility analysis, and regression evidence before merge.

## Engineering truth

1. UI presentation must never invent, hide, or cosmetically alter engineering values to match a reference product.
2. Timestamps, trigger position, sample values, phasors, impedance, protection zones, IEC 61850 quality/state, sequence/order, report facts, and exported evidence preserve their engineering semantics end-to-end.
3. Missing/invalid source data remains explicit as unknown/invalid/NaN or another documented state; it is not silently replaced with plausible engineering values.

## State ownership

4. Each document/session/device workflow has one authoritative state owner. UI mirrors/derives state; it does not become a second protocol or engineering model.
5. A new feature must not create a parallel parser, protocol state machine, waveform store, cursor truth, or report truth merely to bypass an existing defect.
6. Background work may publish validated immutable/bounded results to UI state, but must not mutate UI-owned objects unsafely.

## Responsiveness

7. File, network, device, database, package/update, report-generation, bulk analysis, FFT/harmonic/locus, or other potentially expensive work never blocks the UI thread.
8. Cursor/selection/drag visual feedback remains lightweight and decoupled from full-record recomputation.
9. High-frequency acquisition/events do not map 1:1 to expensive UI renders. Batching/coalescing/backpressure is explicit.
10. No race or lifecycle bug is considered fixed by an arbitrary sleep/delay alone.

## Large data

11. Large records/captures are processed with bounded working sets; architecture must not require multiple eager whole-file copies merely for convenience.
12. Dense waveform/chart rendering scales primarily with visible pixels/viewport, not total sample count.
13. Downsampling for disturbance/protection waveforms preserves extrema/transients; simple averaging must not hide short events.

## Failure handling

14. Expected/recoverable parser/protocol/domain failures use explicit Result/Try/status semantics where practical; exceptions are not normal hot-path control flow.
15. OS/.NET/library exceptions are contained at meaningful boundaries and converted to structured failures.
16. One malformed field/packet/file row or failed optional operation must not crash the entire workstation when isolation is technically possible.
17. Diagnostic infrastructure is observational. A full/slow/broken log or diagnostic sink never blocks critical processing or becomes application failure.
18. High-rate diagnostics use bounded queues/channels with deduplication/rate limiting/aggregation.

## Protocol/device behavior

19. IEC 61850/device state machines have explicit transitions, bounded retries/timeouts, cancellation/shutdown behavior, and safe terminal states.
20. Unexpected/late/malformed traffic cannot silently corrupt current association/session state.
21. Discovery/read workflows never become implicit active write/control operations.
22. Device/network callbacks do not perform expensive UI work directly.

## Resource lifetime

23. Every owned stream, socket, timer, worker, subscription, event handler, cancellation source, mapping, unmanaged buffer, and device handle has an explicit owner and release path.
24. Document/session close or reload cannot leave callbacks/workers publishing into destroyed or superseded state.
25. Queues that can receive sustained traffic are bounded and have an explicit overload policy.

## Regression discipline

26. A bug fix protects the exact reported failure mode with a deterministic regression test/check whenever technically practical.
27. Public/persisted formats, protocol mappings, timing semantics, defaults, and report semantics do not change without compatibility analysis.
28. Existing working capability is not removed or rewritten simply because implementing the new request is easier in a replacement path.
29. Three successive symptom patches in the same subsystem trigger root-cause/architecture re-audit before a fourth patch.
30. Completion requires evidence. Compile success alone is never proof that an engineering workflow is correct.