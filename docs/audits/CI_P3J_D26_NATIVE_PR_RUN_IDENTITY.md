# D26 CI-P3J: exact PR identity for canonical native shadow comparison

Related: [CI cost #542](https://github.com/masarray/arsas/issues/542). No release or deduplication authority.

## Root cause

The advisory COMTRADE native comparator chose canonical Build ARSAS runs by head SHA and head branch only. Multiple PRs may legitimately share the head branch and SHA while targeting different bases, so the newest unrelated synthetic merge run may shadow the correct run. In ARSAS, RC staging PR #727 and main promotion PR #729 both use the same RC source branch. GitHub's `pull_requests[].head.sha` may be updated for old runs: never trust it as immutable provenance.

## D26 fix (isolated RC draft)

Read the exact triggering pull request from the existing GitHub Actions `GITHUB_EVENT_PATH`, cross-check its positive PR number and head SHA/ref against CLI inputs, then require a matching `pull_requests[].number` on candidate canonical runs, in addition to the exact head SHA/branch/event/build workflow. No branch-only fallback. Bound local event JSON to 2 MiB and fail closed on mismatches or missing PR links.

The comparator still verifies exact synthetic merge SHA, engine lock, ArdIrec lock, archive digest, test inventory and native bridge digest. `independentWindowsCTestRetained=true`, `deduplicationAuthorized=false`, and `releasePromotionAuthority=false` remain unchanged. PR identity is included in advisory result metadata.

Five additional offline unittest methods cover cross-PR SHA collision, missing PR association, mismatched event identity, wrong PR online and explicit output provenance. The existing CI P0 workflow already executes the shadow-test script. This patch modifies only two existing Python files plus this note, with no workflow, native build, WPF or engine edits.

## Acceptance and rollback

Do not equate this change with actual CI savings. CI P0 tests, canonical Windows checks, and COMTRADE native acceptance must pass at exact source before owner-approved integration. No changes to parallel PR #757, #759, safety #753-756, Write #726/#716, SG7 #688, GUI #732 or protected RC. Revert only the two script changes if the new PR association contract proves unavailable from the GitHub API; never weaken independent Windows CMake/CTest to accommodate it.
