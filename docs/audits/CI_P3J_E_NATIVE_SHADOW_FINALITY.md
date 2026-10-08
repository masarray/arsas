# CI-P3J-E — Completed-canonical native parity finality gate

Issue #470. Builds on **merged** #475 (real SHA-256 + native 9/9 CTest parity) and **merged** #477 (required COMTRADE native build/CTest and native-only route guard complete on Windows; advisory-only comparison runs on Ubuntu).

## Problem proven in real GitHub Actions

PR #477 run 37758793077 independently built/tested its COMTRADE DLL on Windows in 87 seconds (previous run 37755975584 held the Windows runner for 237 seconds). The Ubuntu observer subsequently compared the exact source/engine/ArdIrec and all nine CTest names plus the binary SHA-256 successfully, but the canonical Build ARSAS run 37758793021 was still **in_progress**. The verifier explicitly marked "provisional: true", "fullCanonicalBuildPassed: false", "deduplicationAuthorized: false".

Although the canonical build ultimately passed, a provisional observation is not evidence that its complete package/integration tests passed. Nor does a green advisory workflow job imply that this was final parity.

## Minimal fail-closed change

- Reuse the existing \`scripts/compare-ci-native-shadow.py\` and read-only \`actions:read\` permission. Add opt-in \`--require-completed-canonical\` mode on the existing Ubuntu observer only.
- Keep the wait bounded to 600 seconds. Require **the latest exact PR-head, branch, synthetic merge-source SHA and locked engine/ArdIrec** Build ARSAS run to be **completed/success** before fetching and verifying its two-file tiny JUnit/proof artifact.
- Any failed/cancelled completed canonical run errors immediately; in-progress/queued runs cannot produce a final verdict; timeout/missing artifact and malformed/foreign proof fail closed. The advisory step continues to flag an inability to establish parity as a **visible warning**, not a promotion/pass claim. Native DLL mismatch still yields no parity.
- Preserve default/provisional verifier behavior for explicit earlier diagnostic consumers; the added finality contract is opt-in and tested, so no unrelated callers change meaning.
- New offline fixtures prove completed success, in-progress timeout, completed failure, missing artifact, and transition from in-progress to completed with no pre-completion artifact lookup.

## Thread-safe scope and cost

No new workflow or runner, no new Windows build, no new evidence transport, no changed engine pin, COMTRADE runtime code, release authority, R10 field baseline, or P7 branch. The **mandatory Windows CMake/CTest and native-only route guard** still run independently before any observer. No topology-budget limit changes.

Wait time increases only on the existing lightweight Ubuntu observer when canonical build has not yet completed. A matching, completed canonical and independently passed native DLL/CTest can now be recorded as **final observation of parity for that one exact revision**. This does not itself authorize retiring the independent Windows native lane or reusing bytes in a release.

## Rollback

Remove the opt-in flag from the Ubuntu job to restore provisional early advisory comparison, or revert this PR entirely; do not change the mandatory native tests or sealed package verifiers. Review multiple independent completed-success parity observations and named managed COMTRADE domain tests before any future deduplication consideration.
