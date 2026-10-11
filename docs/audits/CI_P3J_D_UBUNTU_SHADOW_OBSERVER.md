# CI-P3J-D — Free Windows runner after independent COMTRADE CTest

Issue: #470. Prerequisite: merged PR #475 proved a real same-PR native JUnit inventory and SHA-256 bridge parity while COMTRADE's independent Windows build and source guards stayed mandatory.

## Observed cost (not modeled savings)

GitHub Actions **run 37755975584** (October 8, 2026) on exact PR #475:
- Windows job started 09:21:44 UTC and completed 09:25:41 UTC (**237 seconds**).
- Native CMake configure + compile + CTest: 09:21:58–09:23:05 (**67 seconds**).
- The advisory wait/compare step: 09:23:05–09:25:37 (**152 seconds**), before the canonical build fully completed.
- Canonical **Build ARSAS** run 37755975609 ultimately passed; independent/native CTest inventory was **9/9 equal** and bridge SHA-256 was **fa8efcaf26e8ebb0a29808a2e52ad5c9f466ec2df146945d29c5d662987c41ce**.
- The 152-second wait monopolized a Windows runner only to poll and compare small read-only evidence. Optimization targets this wait **not the required independent native tests**.

## Changes

1. On PR, the required Windows COMTRADE job performs the same pinned CMake build, independent JUnit-emitting CTest, and native-only routing guard. It stages **the actual independently tested** `ctest.xml` plus `ardirec_bridge.dll`, then uploads one small artifact `ARSAS-independent-native-ctest-shadow`. On `workflow_dispatch`, it still independently compiles/tests and enforces routing; no shadow upload.
2. A new **Ubuntu-only observer job** depends on the required Windows job and runs only on PR events. It checks out the exact candidate, downloads those two independent native inputs from **its own workflow run**, verifies they are present, reads the locked engine and ArdIrec SHAs, and calls the unchanged read-only GitHub canonical shadow comparer (bounded 600 seconds).
3. Its only comparison step is marked `continue-on-error: true`; a mismatch or unavailable canonical proof is never treated as established parity and is called out in the job summary. Missing independent inputs are a **hard observer job failure**. The required Windows job never becomes advisory.
4. No new native compilation, no release/installer authority, no runtime/engine code, no changes to physical R10, and no permission to retire native CTest.

## CI budget and multi-thread safety

Explicit tracked topology delta: **one lightweight Ubuntu job**, one checkout, one small artifact upload (`actionsCheckout 36→37`, `actionsUploadArtifact 34→35`). The code does not add Windows jobs, workflow files, CMake calls, or .NET restore/build/test. Observer uses the same PR-specific concurrency group and is cancelled on superseding PR revisions; push/main and manual workflows retain unique-run groups. The GitHub API comparer requires exact PR head/branch, synthetic source merge SHA, engine/ArdIrec SHA, canonical run ID/attempt and tested native hashes. It cannot consume cross-PR or older branch evidence as matching current SHA.

This is expected to release the Windows runner sooner, but **actual after-change timings must be measured**. Total elapsed PR wall-clock may remain dominated by canonical Build ARSAS; reducing Windows occupancy does not imply a faster overall result.

## Rollback

Reinstate the advisory comparison in the same Windows job, remove the Ubuntu observer and independent native shadow input upload, restore topology budget counts. Independent native Windows CMake/CTest and source guards must remain unchanged in either direction.

## Later promotion gate

Multiple completed-success native proof parity observations and explicit COMTRADE managed native test-family evidence are prerequisites before eliminating independent CMake/CTest on PR. The present milestone **does not** authorize that change.