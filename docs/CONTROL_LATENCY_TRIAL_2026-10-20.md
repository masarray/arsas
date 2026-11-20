# Control latency and report isolation trial

Baseline: application `99d39c77bb9d68a5c6d964c731d868196f23f993`, engine `ba21d882f67de22c10d3155b449aac77aa8b4899`.

Field logs show successful commands around 8–13 seconds, mostly inside the native control action, with final feedback reads around 8–14 milliseconds. The old log does not distinguish SBOw response, Operate response and asynchronous termination. No claim is made that this trial removes the entire field delay.

## Changes

- Preserve verified control metadata when reopening the Command Dock. Explicit Refresh remains forced. A resolved status-only object with `-` feedback is not automatically re-inspected.
- Claim inspection ownership before waiting for a local concurrency slot, and release it on cancellation. Busy inspections and commands are excluded from another refresh batch.
- Pin engine PR #183. It rejects non-RCB InformationReport shapes before single-monitor RCB fallback. Control termination/LastApplError remain routed to independent control subscribers.
- Existing ordered control diagnostics now include `serviceStartedUtc`, `serviceMs`, and `postResponseWaitMs`, measured around each local service call. These are not packet-capture timestamps.

## Validation and remaining work

Engine compilation and 64 directly invoked xUnit Fact tests passed locally. The standard test host fails in the execution environment before executing tests; Windows CI remains required. App WPF/runtime and physical acceptance remain pending.

In the next operator command diagnostic, compare SelectWithValue versus Operate service durations and post-response wait. A long service interval does not by itself prove IED delay: it includes transport and scheduling. Retain exact build identities and use wire capture if those stages remain slow. Do not lower timeouts, remove interlock checks, or replace report values with speculative data.

Report startup delay, full CPU/memory profiling, and command-time pause of process-report publication remain unresolved. The monitoring loop currently pauses during active control; the 3 ms report receive slice alone does not explain the 8–13 second native action time.
