# CI-P3J-A — Canonical native ArdIrec CTest evidence

Issue: #470. This is an **evidence instrumentation milestone**, not authorization to delete the independent COMTRADE native test workflow.

## Baseline and discovery

Both `Build ARSAS` and `Validate COMTRADE viewer integration` call the same locked `scripts/build-ardirec-bridge.ps1`, which configures Windows x64 Release ArdIrec with native-only / Qt desktop disabled, builds the bridge, then runs CTest. The canonical build also performs managed packaged-bridge/locus integration tests and seals a file-by-file hashed Windows package. Previously, the manifest's `nativeBridgeIntegrationPassed` boolean was not independent machine-readable proof of the individual native CTest cases.

## CI-P3J-A change

- Add an **optional** `-CTestJunitPath` parameter to the native build script. With the option set, CTest emits JUnit using the existing x64/Release build. Without the option, existing COMTRADE/manual paths keep their original invocation and test behavior.
- Require native test cases to be present, individually passing, non-skipped, nonduplicated and consistent with suite counters. Reject malformed/unbounded/DTD/entity XML. Do not infer native success from an exit code alone.
- Seal `ci-native-ctest-authority.json` against exact ARSAS, ARIEC61850 and ArdIrec SHA; canonical workflow run/attempt/event; CTest JUnit SHA-256; native bridge SHA-256/size and discovered case names.
- Stage `verification/native/ctest.xml` and `verification/native/ci-native-ctest-authority.json` in the existing immutable Windows package input. Its existing archive file list, sizes and SHA-256 seal binds their exact bytes without adding a workflow/artifact.
- Reverify staged native evidence before canonical package sealing. Add Python offline positive and fail-closed fixtures in the CI-P0 gate.

## Non-goals and authority limits

No changes to the ARSAS UI/runtime, ARIEC61850 source/lock, native CMake flags, CTest selection, field tests, COMTRADE independent Windows lane, installer/release promotion rules or existing package verifier schema.

The new proof has `independentComtradeLaneReplaced=false` and `releasePromotionAuthority=false`. Do **not** treat these files as an independent signed attestation; their authenticity follows the exact successfully verified canonical package run/artifact and its file digests.

## Follow-up: CI-P3J-B (separately gated)

1. Confirm actual canonical JUnit test inventory and CTest names from Windows CI.
2. Add a read-only verifier that rejects missing CTest proof, wrong source/engine/ArdIrec/run ID, modified bridge, absent managed COMTRADE/bridge/locus domain tests and failed latest canonical runs.
3. Compare the independent COMTRADE lane against the canonical lane on the same SHA, including native CTest coverage, compiler flags and binary identity.
4. **Only after real parity is green**, consider running pull-request COMTRADE static routing checks on Ubuntu while retaining Windows CMake/CTest for manual diagnosis. Otherwise retain independent Windows CI permanently.

Rollback: revert the optional CTest JUnit output and canonical verification staging. The native build script default CTest path and all existing release/physical authority remain intact.
