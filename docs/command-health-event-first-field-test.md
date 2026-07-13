# Command, liveness, and event-first field test

Validate this follow-up with the IED connected and reporting active:

1. When the breaker is Open, the Open button is disabled and clicking through stale UI state must produce no duplicate SBOw/Operate sequence.
2. A valid Close followed by Open must each issue exactly one command sequence and reach positive CommandTermination/process feedback.
3. Power off or unplug the IED. The relay icon should turn red after two bounded MMS health-probe failures (normally about two seconds), while smart reconnect remains active.
4. Restore the IED. The app should reconnect, re-arm report acquisition, and return the icon to green.
5. Confirm report-covered values show report acquisition as primary; per-point MMS reads should be low-rate verification rather than continuous primary polling.
6. Confirm the Visual Studio Output window no longer floods `IsRecentlyChanged` binding errors for `SignalDefinition` rows.
