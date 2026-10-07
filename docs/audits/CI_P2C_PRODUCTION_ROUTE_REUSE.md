# CI-P2C — Canonical regression reuse in production-route guard

Issue #436. Existing accepted milestones: P2A #431 and P2B #433.

## Scope

On pull requests, Smart Discovery Production Promotion Guard independently verifies
the physical-proven Smart Discovery route before and after checking exact-SHA
canonical Build ARSAS full-regression evidence. This is a CI execution
optimization, not production release permission or a new field acceptance.

## Independent checks retained

- Checkout exact triggering synthetic PR merge SHA and assert git HEAD.
- Validate immutable engine lock without substituting historical physical SHA.
- Run enable-smart-discovery-capture.ps1 -VerifyOnly before proof consumption.
- Verify the route and critical reset/dispose source markers.
- Preserve NativeIec61850Client.cs SHA256 before/after evidence verification.
- Run -VerifyOnly again and reject workflow/promotion dependent targets.

## Exact canonical proof required

The unchanged scripts/verify-ci-canonical-reuse.py rejects:
- wrong PR branch/head SHA or synthetic merge SHA;
- wrong engine lock SHA, stale or failed newest canonical run;
- ambiguous/missing artifacts, malformed or unsafe archives;
- altered TRX bytes, bad test counters or nonzero failures/skips;
- invalid API provenance or unbounded evidence.

The tool uses read-only Actions API and a bounded waiting policy.
Manual workflow_dispatch retains reusable full regression separately.
Packaging, field capture, R10 independent engine testing, release and
production promotion remain owned by existing workflows.

## Inventory and rollback

Static source cost from P2B to P2C: dotnet restore/build/test 14/14/16
to 13/13/15; setup-dotnet 12 to 11; setup-python 14 to 15.
Historical P0 observations are unchanged. Lowered limits are recorded in
evidence/ci-workflow-budget.json.

If exact cross-run matching is unreliable, revert the workflow migration
and restore direct dotnet regression execution; never drop route assertions
or rewrite historical IEC 61850 physical evidence.
