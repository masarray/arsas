# CI-P2E — Exact-main canonical ARSAS regression reuse

Issue #440.

## Goal

Remove the duplicate ARSAS application restore/build/full-test from
Smart Discovery Post-Merge Production Verification while preserving:

- exact R10 physical convergence assertions;
- exact merged ARIEC61850 checkout;
- independent engine restore/build/full-test;
- Build ARSAS portable publish/smoke authority;
- post-merge diagnostic attestation.

## Canonical main proof

The shared verifier now accepts an explicit workflow event identity. Its default
remains pull_request for existing P2A-P2D consumers. P2E calls it with:

- event-name=push;
- branch=main;
- head SHA equal to GITHUB_SHA;
- source SHA equal to that same GITHUB_SHA;
- exact immutable engine lock SHA;
- artifact-ready mode enabled.

The artifact must still contain the exact canonical manifest and TRX, exact
run ID/attempt, matching SHA-256 digest and an entirely green test suite.
A completed failed Build ARSAS run is never reusable.

## Attestation

R10-post-merge-production.json remains schemaVersion 1 for compatibility and
adds explicit fields showing whether independent engine regression and canonical
ARSAS regression were actually verified. Because the artifact is uploaded with
if: always(), verificationStatus becomes incomplete-or-failed if either proof
did not complete. A diagnostic artifact therefore cannot imply acceptance.

## Static CI cost

Compared with P2D, workflow-source dotnet restore/build/test counts fall from
13/13/15 to 12/12/14. setup-dotnet is unchanged because the post-merge job still
runs the engine suite. setup-python increases from 15 to 16 for the pinned
canonical evidence verifier.

## Authority boundary

Canonical regression reuse proves ARSAS application tests only. It does not
replace the R10 engine suite, physical field evidence, portable packaging,
installer validation or release promotion.

## Rollback

Restore the direct ARSAS main restore/build/test step in the post-merge workflow.
No evidence format, engine test or physical authority must be weakened to roll
back this optimization.
