# CI-P3H — Superseded PR cancellation

Issue #464.

## Goal

Stop obsolete Windows CI for stale pull-request revisions without weakening
main, tag, manual, installer, package, or release authority.

## Scheduling contract

Ten Windows-heavy pull-request workflows use:

- group: workflow name + pull-request number;
- fallback group identity: unique github.run_id for non-PR events;
- cancel-in-progress: true only when event_name is pull_request.

This means a synchronize event for the same PR cancels the older revision,
while push/main, tags, release production paths, and workflow_dispatch runs
cannot cancel one another through this policy.

## Why this matters

P2/P3 consumers bind to exact source, engine, run and artifact identity. Keeping
obsolete PR Windows runs alive wastes capacity and can later produce irrelevant
red statuses after a newer canonical Build ARSAS run becomes authoritative.
Cancellation makes the newest PR head the sole active revision while preserving
fail-closed exact-SHA verification.

## Affected workflows

- Build ARSAS;
- Windows installer validation;
- Smart Discovery Mainline Readiness;
- RCB export guard;
- SV evidence validation;
- IO testing validation;
- COMTRADE integration;
- Smart Discovery Golden Provenance;
- Smart Discovery Golden Budget;
- Smart Discovery Repeat-Run Stability.

Existing workflows that already had an intentional concurrency contract remain
unchanged.

## Authority boundary

P3H changes scheduling only. It does not change:

- canonical Build ARSAS regression/package evidence;
- sealed package or portable hashes;
- physical R10 authority;
- installer compile/install/runtime smoke;
- validated-installer authority;
- release checksums, SBOM, provenance, attestations, or publication;
- manual recovery paths.

## Regression guard

`scripts/test-ci-workflows.py` asserts the exact PR-only concurrency expressions
for all ten workflows and confirms the production release workflow does not gain
the PR cancellation policy.

## Rollback

Remove the P3H concurrency blocks. Never replace the event-scoped expression
with unconditional `cancel-in-progress: true` on workflows that can run on main,
tags, or manual recovery events.
