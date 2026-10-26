# D21 CI-P4 documentation-only PR admission canary

This isolated, expendable markdown-only change is used to validate the existing `Build ARSAS` workflow's read-only `pr-scope` GitHub API path classifier on a genuine pull_request event.

- Base: `ci/p4-failclosed-optimized-gates-d19-20261027` at `e0c9724b203ac38f2a947d71795ac1173ca5ed5b`.
- Expected changed paths: exactly this newly added `docs/maintenance/*.md` file.
- Expected `pr-scope`: SUCCESS, `Changed paths=1, Windows required=false`.
- Expected canonical `build-windows`: SKIPPED, with no sealed Windows artifact on this canary SHA.
- The canary is not physical evidence, not a release artifact, and must never be merged to the RC, stable or main.

After logging the exact CI evidence, this short-lived draft PR should be closed without merge. Keep the CI-P4 implementation PR #757 and all parallel-owner branches unchanged.
