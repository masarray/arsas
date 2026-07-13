# Close feedback event verification

1. Clear diagnostics and start dynamic monitoring.
2. Alternate Open and Close commands while observing the physical IED, IEDScout, and ArIED Live Monitor.
3. Live Monitor must change immediately from command-confirmed feedback.
4. A matching `dchg` report must then log `event-driven report confirmed command feedback` within two seconds.
5. The monitor must not roll back to an older state from a stale report frame.
6. If event delivery is missing, diagnostics must report `no matching dchg report arrived within 2 seconds` instead of silently waiting for MMS validation.
