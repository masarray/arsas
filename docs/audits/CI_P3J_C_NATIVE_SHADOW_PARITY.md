# CI-P3J-C — Shadow independent ArdIrec native test/byte parity

Issue #470. Follows merged PR #472 (native CTest evidence producer) and PR #474 (archive verifier, current installer/Field Capture required proof).

## Intent
Do not assume running the same CMake command is equivalent to running the same native tests and producing the same DLL. Compare *both* the CTest case inventory and SHA-256 of the independently built native bridge against canonical proof **for one exact source/engine/ArdIrec identity**. A mismatch stays visible; the independent Windows COMTRADE lane is still a required gate.

## Data flow
1. Canonical Build ARSAS still tests the exact native bridge and seals `verification/native/ctest.xml` plus `ci-native-ctest-authority.json` in the full package. For PRs only, it also uploads a **tiny two-file copy** as `ARSAS-native-ctest-shadow` after CTest, without waiting for package/installer completion.
2. COMTRADE's **existing independent Windows job** now runs CTest with optional JUnit output; CMake flags and exact locked native source stay unchanged. Its existing native-only source routing assertions execute before comparison.
3. The read-only shadow comparer discovers the latest Build ARSAS run for the exact PR head/branch, requires the exact PR synthetic merge SHA and ARIEC61850/ArdIrec SHA in its proof, validates the GitHub artifact ZIP metadata digest and a bounded two-file ZIP, independently checks the JUnit/proof contents, then compares test names and independently built DLL size/hash.
4. A canonical run still in progress is marked **provisional**, not completed-success. No binary reuse, independent-native retirement, installer, release, or field-physical authority follows from provisional or advisory parity.
5. Comparison runs on the **existing COMTRADE Windows job** without new jobs. Its outcome is `continue-on-error` only for the **new advisory comparison step**. Any core native CTest failure and existing native-only routing failure are still hard CI failures.
6. Diagnostics are recorded in the workflow job's step summary. A digest difference or unavailable canonical proof must never yield a "pass" parity claim.

## CI budget and rollback
One additional tiny artifact upload is deliberately budgeted (`actionsUploadArtifact: 33 -> 34`), with no new workflow, Windows job, restore/build/test, checkout, or runtime dependency. It avoids a second transfer of the existing ~165 MB canonical Windows package on every COMTRADE comparison. The artifact is scoped to PRs with 14-day retention.

Rollback removes the small canonical native-shadow upload and advisory COMTRADE comparison only. Preserve the independently passing native CMake/CTest, canonical package/installer identity, physical R10 checks and source pinning.

## Promotion gate after observations
Do not delete the independent COMTRADE Windows build just because an isolated shadow comparison returns equivalent hashes. Inspect several **completed-success** exact-source independent comparisons and runtime/runner efficiency data, reject flaky/unavailable or mismatched cases, and get explicit review before replacing its regression authority.
