# P0-T — command-priority control inspection scheduling (trial, 2026-10-20)

## Exact source and physical evidence

Field diagnostic (2026-10-20 13:27:52 +07) used ARSAS \`99d39c77bb9d68a5c6d964c731d868196f23f993\` with engine \`ba21d882f67de22c10d3155b449aac77aa8b4899\`, one connected SIEBCU association. No site identifier, IP address, capture or raw diagnostic is stored in this repository.

Three confirmed SBO enhanced commands:
- CSWI1 Open: UI click-to-result 7,964.429 ms, native control 7,808.130 ms, final feedback 13.675 ms.
- CSWI2 Close: UI click-to-result 8,099.409 ms, native control 7,973.275 ms, final feedback 8.436 ms.
- CSWI1 Close: UI click-to-result 13,323.694 ms, native control 13,224.153 ms, final feedback 8.731 ms.

All three reported two MMS control service responses, positive CommandTermination, and matching final process feedback. The old diagnostic cannot distinguish SBOw MMS-response latency, Operate MMS-response latency, or post-acceptance CommandTermination delay. **Do not attribute 8–13 seconds to UI, relay, or wire RTT without the stage measurement introduced by engine draft PR #183.**

## Source-proven concurrency issue and correction

The existing WPF Command Dock refresh launched a \`Task.WhenAll\` over all selected control targets with a local three-request semaphore. The native client \`InspectControlAsync\` serializes *every* inspection and command through \`_controlCommandGate\`, so there is no native throughput benefit from the three-way UI queue. It places unrelated control inspections ahead of operator-initiated Operate. When the dock reopens repeatedly, it can also compete for the same control admission slot.

This companion patch (stacked on consumer PR #635) makes read-only inspection deliberately sequential, keeps one UI refresh owner per device, rechecks \`NeedsInspection\` just before each request, and yields to WPF input priority before scheduling another MMS inspection. When any same-device command is active, no additional inspection begins. The one inspection already admitted may finish before a command; this change **does not preempt a wire request**. The native service still serializes the association; no priority queue, parallel MMS write, arbitrary delay, or new process-value cache is introduced.

## Safety, synergies and limits

- No changes to SBOw/Operate/Cancel, \`ctlNum\`, service timeout, interlock, synchro, Test, CommandTermination, process feedback verification, or uncertain-wire single-flight/quarantine.
- Explicit Refresh still requests forced inspection when no refresh is active. If a refresh is already active, the UI clearly states that the operator can retry on completion rather than silently duplicating MMS requests.
- This patch intentionally does **not** touch Browser, FC=SV/OR/BL, IED-card progress, reporting projection, or SCL leaf datasets. Consumer leaf PR #636 owns those files.
- Source-contract regression covers sequential admission, UI input yield, command busy exclusion, refresh lifetime, and retained native safety calls.
- Expected queue improvement: 0 *pre-enqueued* parallel WPF inspections instead of up to 2 waiting behind an active inspection. **Not a measured 8–13 s latency reduction.**
- Physical validation still required using one exact combined SHA. Capture engine PR #183 ordered \`serviceMs\` and \`postResponseWaitMs\` for SBOw/Operate, plus actual Ethernet capture if service time remains high. Windows WPF build, full regression/CI and relay FAT are mandatory prior to merging/releasing.

## Test plan

1. Run Build ARSAS Windows workflow and \`P109S\`/\`P109T\` regression tests on the exact head.
2. Connect the relay, expand/collapse Command Dock repeatedly and verify verified StatusOnly rows aren't read again.
3. With background read active, stage and confirm a different operable CSWI command. Observe no newly queued inspection after command claim.
4. Verify one MMS SBOw/Operate pair and positive termination; no second/automatic retry. After reconnect, verify fresh inspection and status.
5. Repeat exactly the previously slow three commands and compare stage timings, not only total click-to-result.

Draft only; not field-qualified.
