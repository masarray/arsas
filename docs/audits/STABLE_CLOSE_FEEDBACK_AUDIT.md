# Stable Close process-feedback audit

## Live evidence

The OLSF501 diagnostic capture shows that the IEC 61850 control service itself is fast and successful, but the first Close feedback sample is not the settled equipment state:

- Close request started at `05:22:28.547`.
- Positive enhanced-security completion was returned at `05:22:28.968`.
- The reported feedback elapsed time was only `0.908 ms`.
- At `05:22:33.025`, roughly four seconds later, MMS validation detected the actual position change that had not been delivered by the armed report.

This matches the observed screen sequence: a very short Closed indication, a return to Open, then the final Closed indication after the breaker mechanism/process logic completes.

## Root cause

`CSWI1.Pos` is both the command object and a status-bearing object. Immediately after `SBOw → Operate → CommandTermination`, the relay can expose a short command-object echo matching the requested value before the stable process/equipment status settles.

The `0.908 ms` feedback result is therefore not credible as mechanical breaker travel. Positive CommandTermination proves that the enhanced-security command completed; it does not prove that the primary-equipment position has already settled.

## Why the first workaround made it worse

The first workaround classified the echo only after the control-result diagnostic was emitted. By then the Closed value had already entered the normal WPF point-update queue. The command row also retained a deferred Closed value while `ControlIsBusy=true`, so that optimistic value could be published again when the command completed and remain visible until the next real live update.

That implementation reacted too late and operated only on the command-row model. It did not stop the transient sample before the main Value Viewer.

## Corrected fix

The correction now works at the source of the UI update:

1. Replace the normal `PointUpdated` subscriber with a thin stability filter before snapshots enter the 100 ms Value Viewer batch.
2. Keep Open, Intermediate, Bad, XCBR, and XSWI position updates immediate.
3. Debounce only `CSWI*.Pos.stVal = Closed` for 350 ms.
4. Cancel the pending Closed snapshot immediately when Open/non-Closed follows, so the short command echo never reaches the Value Viewer.
5. Mark a Close as stable only after the debounce survives.
6. Suppress direct command-result Closed assignments until that fresh stable live-position evidence exists.
7. Keep the last real state visible and show `Command accepted — waiting for stable Closed process feedback…`.
8. Expire the waiting state after 15 seconds without issuing any retry or second command.

Open is intentionally not delayed because the relay's Open movement and feedback are genuinely fast.

## Safety

- No automatic command retry.
- No second Select/SBOw or Operate.
- No change to ctlNum, origin, Test, Check, interlock, synchrocheck, or CommandTermination handling.
- Raw Event Log/SOE remains available; only the live Value Viewer and command-row presentation reject the short CSWI command echo.
- The ARIEC61850 command sequence remains authoritative.

## Live retest

- Open: value changes quickly without added guard delay.
- Close: the Value Viewer and command row remain Open while the short CSWI echo occurs.
- Final Closed appears after the real stable report/poll value arrives, expected around four seconds on this OLSF501 configuration, plus only the 350 ms stability window.
- XCBR/XSWI equipment-position updates are not delayed.
- Diagnostics continue to show CommandTermination separately from stable process feedback.
