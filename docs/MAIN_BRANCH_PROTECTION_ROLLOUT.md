# Main branch protection rollout

Status: **preflight only; branch protection is not yet enabled**. Audited against `masarray/arsas` at `706398fb1977411f80b3398edb43a096ec40be7b` on 2026-09-25.

This is a maintainer runbook, not permission to bypass release/field acceptance. The published v1.6.40 tag, accepted source and existing assets remain immutable. Complete the workflow migration and verify repository permissions before turning on a ruleset.

## Current CI and writer inventory

- `main` currently reports `protected=false`; `.github/CODEOWNERS` exists, but ownership is not enforced by an active required-review rule.
- `.github/workflows/build.yml` runs on every PR to `main`, with the job/check name `Build, test, validate and package Windows application`. It verifies exact `GITHUB_SHA`, source-clean fixtures, application regression tests, real portable packaging and smoke testing.
- `.github/workflows/smart-discovery-post-merge-production.yml` runs on `main` push; `Verify R10 convergence authority on main` is a **post-merge** check, not a check to make universally required before a PR merges.
- The Smart Discovery PR workflows use path filters. Requiring any filtered job for every PR without an always-running aggregate would block unrelated documentation-only changes.
- `.github/workflows/sync-release-documentation.yml` commits landing release records and directly executes `git push origin HEAD:main` (step `Update public landing release records`).
- `.github/workflows/release-windows.yml` uses the contents API to write `.release/published.json` with `branch=main` after publication.
- `.github/workflows/sync-release-evidence.yml` and `.github/workflows/publish-verified-release.yml` update the separate `release-evidence` branch for updater evidence; that is a distinct destination and must remain auditable.
- `.github/workflows/pages.yml` deploys Pages through its environment, rather than pushing the generated site tree to `main`.

## Sequence before enforcing protection

1. **Release-writer migration.** Replace the direct `main` push and contents-API write in the two workflows above with explicitly reviewed publication-record changes or another narrowly documented, tested integration route. Preserve immutable tag/source/asset identity and ensure an automated release does not stall silently behind a new rule. Avoid a blanket bot bypass. Test the new release-record path with non-stable/synthetic evidence first.
2. **Stable PR gate.** Keep the exact-head Windows application build/check name present for all PRs. Define a deterministic always-running aggregate if making path-filtered Smart Discovery checks required; do not mark a job required if the event can legitimately skip it. Verify checks actually report on the PR merge candidate.
3. **Access preflight.** Verify that the maintainer can still update release metadata, close Dependabot PRs and maintain the separate `release-evidence` branch. Decide whether an independent reviewer can be required; do not configure an impossible review rule for a sole maintainer.
4. **Staged ruleset.** In repository Settings, first test the chosen rule on a non-protected temporary integration branch or evaluate-only ruleset if available. Then enforce PR-based changes, non-force-push/non-deletion, required exact-head build, conversation resolution and CODEOWNERS review only where feasible. Scope any deployment/environment and release-tag protection separately.
5. **Acceptance and rollback.** Test an ordinary docs PR, a C#/XAML PR, a Dependabot PR, Pages deployment and the complete authorized release metadata path. Confirm `main` post-merge Build + R10 checks, unchanged pinned engine, and unchanged already-published assets. Record the ruleset URL/settings and measured check behavior in the maintenance issue. If a release writer is blocked, revert the ruleset via repository Settings rather than force-pushing or mutating an existing stable release.

## Do not assume

A green PR does not replace physical acceptance for SCL candidate #374. A passing clean-room/asset manifest gate is not proof of asset licensing or original visual expression. The existence of a CODEOWNERS file alone is not branch protection. Do not turn this runbook into a claim that protection is enabled before verifying the live repository setting.
