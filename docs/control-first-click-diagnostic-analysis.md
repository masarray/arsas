# Control first-click and dynamic report analysis

The field diagnostic records successful native control sequences around 420–426 ms, but also records CB position changes discovered by MMS validation rather than the armed dynamic report. This follow-up separates UI click, runtime queue, native control, and feedback timing; prioritizes control traffic; and enforces dchg/qchg/dupd trigger options on the selected dynamic RCB.

Safety boundary: no automatic Operate retry and no hidden duplicate SBOw/Operate sequence.
