# P8 SIEBCU first-connect / BIT STRING / false reporting warning audit (2026-11-05)

## Operator evidence (P8 trial, SHA 37165a0c / engine ae1ce23d)

Diagnostics generated at 10:53:49 WIB (2026-11-05) for SIEBCU 192.16.1.192:102. Log starts association at 10:53:25.055 and again at 10:53:35.128, with MMS initiated at 10:53:35.182. This establishes two attempts, **not the cause of the first failure**; the retained log does not include a first-attempt exception/rejection. The accepted route is SAS-Port 3 with 192.16.1.92/24, same subnet. The successful smart discovery took ~1756 ms for 211 requests. The trial received 14 Report frames across seven exactly routed RCBs, with seven visible selected values.

At 10:53:42.741, an observer still logged "no InformationReport traffic" despite decoder-based per-RCB evidence showing 7/7 streams with frames at 10:53:41. This is an application diagnostic contradiction, not a proof of wire SOE loss.

The UI displayed `USER2.LOCREM.stVal` as `bits(80, unused=6)`. This raw MMS renderer means one byte 0x80 with six unused padding bits, therefore two significant bits `10`. It does **not** establish a vendor-specific Local/Remote label nor general DPC/Close semantics.

## Source-supported defects and changes

1. Full discovery started its MMS association only once; the cached reconnect route already had a bounded retry. Initial discovery now retries at most once **only** after TCP timeout, connection refused, transport closure, or internal native association cancellation with the parent request still active. It uses a new native client, observes a 500 ms backoff, preserves failure-kind and elapsed-attempt logs, and performs discovery only after the successful association. Explicit cancellation, ACSE rejection, unknown failure and unreachable-network errors do not provoke hidden retries. No reporting/RCB write or control is triggered by retry.
2. The report value formatter already understood valid `bits(HH, unused=6)` but used the decoding only for DPC/position. Exact two-bit non-position values now show `Bits [10]`, without a guessed semantic label; DPC positions retain `Close [10]`. Malformed padding and non-two-bit representations remain unmodified. The optional `BIT` badge is based only on an explicit BitString data-type declaration.
3. The three-second static-report observer used `trafficObserved && HasReportStream` to decide whether to log "no traffic", even when exact per-RCB continuity evidence contained real frames. Now it examines exact routed frame evidence for each RCB in the current association before deciding the verdict; zero-frame peers remain independently unverified. It performs no MMS I/O.

## Safety and acceptance

Changes are scoped to the existing P8 trial branch consumer; ARSAS main, integrated RC, stable v1.6.42, pinned engine baseline, and ARIEC staging/P9 remain unchanged. This change does not prove the first attempt's physical cause, instant first-attempt success, uninterrupted SOE, or SG7 77/77, 4/4 recovery. Validate exact-head CI, then create a separately identifiable Windows nonrelease trial before field retest. Retain raw MMS typed evidence alongside operator display.
