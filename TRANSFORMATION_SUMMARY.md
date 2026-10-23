# ArIED 61850 v1.6.5 transformation summary

This release focuses on connection-failure clarity and support diagnostics.

Key changes:

- TCP refusal, timeout/unreachable, transport reset, and ACSE/MMS rejection are classified separately.
- A transport refusal no longer presents `BalancedApTitle` as though it were the root cause.
- Failed-session native evidence is retained after the session is disposed.
- The Diagnostics tab now includes **Copy Diagnostic**.
- The copied report contains app/engine versions, active network adapters, IED states, TCP probes, association/discovery evidence, and recent logs.
- The diagnostic workflow remains non-modal and never auto-opens the Diagnostics tab.
- App-only package; the user's ARIEC61850 repository is not replaced.
