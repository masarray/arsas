# CI-P3J-B — Fail-closed verification of sealed native CTest evidence

Issue: #470. Depends on merged CI-P3J-A PR #472 (canonical CTest JUnit + source/engine/ArdIrec/DLL identity proof).

## Scope

Verify the two evidence files staged inside the already sealed \`ARSAS-windows-package-input\` artifact:

- \`verification/native/ctest.xml\` — executed Windows x64 Release native CTest inventory.
- \`verification/native/ci-native-ctest-authority.json\` — SHA-256 of JUnit and native DLL, source/engine/ArdIrec SHA, run ID/attempt/event, exact test names and no-skips/no-failures verdict.

After existing archive-level path safety, file count, and per-file SHA-256 checks, the **independent consumer** recomputes the native proof and rejects contradictions. Both files must be present together. New \`--require-native-ctest-proof\` forces their presence for current PR installer and Field Capture package consumers. A missing pair remains readable in default mode solely for historical/manual compatibility. A present but invalid pair always fails, including default-mode release consumption.

## Safety boundary

- No app/engine/runtime changes, no removal of the independent Windows COMTRADE CMake/CTest gate.
- No change to the canonical native compiler flags or test set.
- No new workflow, artifact upload, expensive build or runner required; the verifier inspects bytes it already downloads and checks.
- Native evidence has no standalone release promotion authority. The existing exact canonical Build ARSAS artifact and successful run checks remain required.
- Release workflow's regular canonical path verifies native proof **when present**, without adding a new explicit required-presence policy to historical/manual release paths.
- Installer and Field Capture demand the new proof on PR/push canonical paths; manual fallback remains an independent native build.
- A verified CTest proof is **not yet proof that independent COMTRADE and canonical native builds produce identical bytes**. No deduplication authorized.

## Fail-closed offline fixtures

- Successful source/engine/ArdIrec/run/bridge/test-inventory agreement.
- Missing native pair rejected in strict mode; partial pair rejected in all modes.
- Rehashed/rewritten proof with wrong source/engine/ArdIrec/run/bridge/authority values rejected.
- Rehashed JUnit containing native failures rejected.
- Byte-level tampering already rejected by the package manifest digests.

## Next: CI-P3J-C shadow parity

Capture independently produced COMTRADE native CTest test inventory and native bridge digest on **exact same PR head**, compare to canonical (without conflating source head SHA with GitHub's synthetic merge SHA), measure runner occupancy, and retain independent Windows tests unless proven equivalent. A read-only lightweight proof transport is preferable to re-downloading the 160+ MB sealed Windows package for each comparison.
