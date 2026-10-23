# Control first-click and dynamic report analysis

The field diagnostic records successful native control sequences around 420–426 ms, but also records CB position changes discovered by MMS validation rather than the armed dynamic report. This follow-up separates UI click, runtime queue, native control, and feedback timing; prioritizes control traffic; and enforces dchg/qchg/dupd trigger options on the selected dynamic RCB.

Safety boundary: no automatic Operate retry and no hidden duplicate SBOw/Operate sequence.

## Field verification

1. Clear diagnostics, then issue alternating Open and Close commands.
2. Every physical click must immediately create `Control click accepted` followed by `Control intent accepted`.
3. A state-changing click must produce exactly one SBOw → Operate sequence.
4. A redundant click may return `Already at requested state`, but must not issue Operate.
5. Compare click-to-result, client-total, engine-total, control, and feedback timings to locate any remaining delay.
6. Confirm CB position changes arrive from the dynamic RCB without the `MMS validation detected a value change not delivered by the armed report` warning.
