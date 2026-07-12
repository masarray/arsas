# Stable Close process-feedback audit

## Live evidence

The OLSF501 diagnostic capture shows that the IEC 61850 control service itself is fast and successful, but the first Close feedback sample is not the settled equipment state:

- Close request started at `05:22:28.547`.
- Positive enhanced-security completion was returned at `05:22:28.968`.
- The reported feedback elapsed time was only `0.908 ms`.
- At `05:22:33.025`, roughly four seconds later, MMS validation detected the actual position change that had not been delivered by the armed report.

This matches the observed screen sequence: a very short Closed indication, a return to Open, then the final Closed indication after the breaker mechanism/process logic completes.

## Root cause

`CSWI1.Pos` is both the command object and a status-bearing object. Immediately after `SBOw → Operate → CommandTermination`, the relay can expose a short command-object echo matching the requested value before the stable process/equipment status settles. The previous UI accepted the first matching value as final feedback.

The `0.908 ms` feedback result is therefore not credible as mechanical breaker travel. Positive CommandTermination proves that the enhanced-security command completed; it does not prove that the primary-equipment position has already settled.

## Fix

For position Close commands only:

1. Detect a matching feedback sample reported within 150 ms.
2. Treat that first sample as a possible CSWI command-object echo.
3. Keep the last stable position visible and show `waiting for stable Closed process feedback`.
4. Accept the requested state after either:
   - a non-target sample followed by the final target state; or
   - the target remains present beyond a 750 ms guard window.
5. Time out the UI stability state after 15 seconds without issuing any retry or second command.

Open is intentionally not delayed because the relay's Open movement and feedback are genuinely fast.

## Safety

- No automatic command retry.
- No second Select/SBOw or Operate.
- No change to ctlNum, origin, Test, Check, interlock, synchrocheck, or CommandTermination handling.
- The change is display/feedback stabilization only; the ARIEC61850 control sequence remains authoritative.

## Live retest

- Open: value should change quickly without added guard delay.
- Close: the row should remain Open/Closing while the process is moving; it must not flash Closed for about 100 ms.
- Final Closed should appear when the stable report/poll value arrives, expected around four seconds on this OLSF501 configuration.
- Diagnostics should still show positive CommandTermination separately from stable process feedback.
