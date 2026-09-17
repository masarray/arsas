# P0-5h — Mainline Merge Execution & Post-Merge Production Verification

P0-5h is the execution phase after P0-5g reaches `READY_FOR_REVIEW`. It does not weaken or bypass P0-5f/P0-5g evidence requirements. If physical authority is missing, the phase remains blocked and neither PR is merged.

## Preconditions

All of the following must be true on the exact final heads before merge execution:

- P0-5f authority is `physical-finalized` and production evidence only;
- P0-5g promotion authority is `production-promoted`;
- `SmartDiscoveryProductionPromoted=true` and props are bound to the exact promotion-authority SHA-256 and validated engine head;
- P0-5g readiness is exactly `READY_FOR_REVIEW` with zero blockers;
- Smart Discovery Production Promotion Guard succeeds;
- Smart Discovery Mainline Readiness succeeds;
- Smart Discovery Field Capture Build, Golden Budget Lock, Golden Provenance, Repeat-Run Stability, generic Build ARSAS, installer/IO/SV/legacy guards succeed;
- engine PR #134 exact head CI succeeds;
- both PRs are open, mergeable, and have no unresolved review threads.

## Why merge commits are mandatory

P0-5h uses GitHub merge method `merge` for both repositories. Squash or rebase are not accepted because the physical and promotion evidence bind exact PR head commits. A merge commit preserves those validated commits as ancestors of `main`, which can be verified after merge.

## Merge manifest

After P0-5g is fully ready, generate `evidence/smart-discovery-mainline-merge-manifest.json` with `new-smart-discovery-mainline-merge-manifest.ps1`.

The manifest records:

- P0-5g validated ARSAS head;
- exact engine PR head;
- both base SHAs at authorization time;
- physical authority, promotion authority, readiness, target, and promotion-props hashes;
- merge method `merge`;
- merge order `engine -> arsas`.

The tracked manifest intentionally does **not** store its own future ARSAS commit SHA. After adding the manifest, the only allowed post-authorization ARSAS change is that manifest file itself. At merge execution time the live PR head is resolved again and supplied to GitHub as `expected_head_sha`.

## Exact execution sequence

1. Re-fetch ARIEC61850 PR #134 and ARSAS PR #324.
2. Require each PR to still be open and mergeable.
3. Require current base SHA to equal the SHA captured by the P0-5h manifest.
4. Require no unresolved review threads on either PR.
5. Require current engine PR head to equal the manifest engine `ExpectedHeadSha`.
6. For ARSAS, compare the P0-5g validated head to the live PR head. The only changed path allowed is `evidence/smart-discovery-mainline-merge-manifest.json`.
7. Merge engine PR #134 first using merge method `merge` and its exact `expected_head_sha`.
8. Verify the engine merge succeeded and the validated engine head is now an ancestor of engine `main`.
9. Re-fetch ARSAS PR #324. Abort if its head/base/mergeability/review state changed.
10. Merge ARSAS PR #324 using merge method `merge` and the freshly resolved live ARSAS head as `expected_head_sha`.
11. Never enable auto-merge in this phase.

Any mismatch aborts execution. There is no force or fixture bypass.

## Post-merge production verification

`Smart Discovery Post-Merge Production Verification` runs on pushes to ARSAS `main` and can also be dispatched manually. It:

- requires the tracked P0-5h merge manifest and P0-5g promotion authority;
- clones current ARIEC61850 `main`;
- verifies the exact validated engine head is an ancestor of engine main;
- verifies the P0-5g validated ARSAS head is an ancestor of ARSAS main;
- verifies production promotion props remain enabled and hash-bound to the promotion authority and validated engine head;
- runs engine source hygiene, restore, build, and tests from engine main;
- runs ARSAS restore, build, and tests from ARSAS main;
- emits `P0-5H-post-merge-production.json` as the post-merge attestation artifact.

P0-5h is complete only when both PRs are merged in the required order and this post-merge attestation reports `Verdict=PASS` from mainline state.

## Current blocked state

Until a real P0-5f physical-finalized authority exists, P0-5g remains `BLOCKED`, no P0-5h merge manifest may be authorized, and neither PR may be merged by this phase.
