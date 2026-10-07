# CI-P2B — Mainline Readiness without duplicate application compilation

Companion: [CI-P2A canonical exact-SHA reuse](CI_P2A_CANONICAL_REGRESSION_REUSE.md).
Issue: [#432](https://github.com/masarray/arsas/issues/432).

## What changed

`Smart Discovery Mainline Readiness` already performs an R10 physical acceptance
audit and independent **ARIEC61850 engine** restore/build/full tests. Previously
it also repeated the entire ARSAS application restore/build/full tests, which
canonical `Build ARSAS` runs unconditionally on the same PR merge tree.

CI-P2B preserves **all** original R10 physical convergence predicates and
the independently checked-out exact engine head and engine test suite.

Only the duplicate *application* restore/build/test portion is replaced with
the reviewed CI-P2A `scripts/verify-ci-canonical-reuse.py` consumer.

That consumer can pass only if the latest matching canonical Build ARSAS
`pull_request` run:

1. completed successfully, including packaging and portable smoke;
2. has the same PR head SHA and branch;
3. produced all-pass canonical TRX evidence for **this exact synthetic merge SHA**;
4. used exactly the same immutable engine commit that passed R10 physical
   convergence checks;
5. published a manifest matching run ID, attempt, TRX SHA-256 and test counts.

Failures, missing evidence, stale merges and API/timeouts still fail closed.
Branch-protection settings are not assumed. Both check names remain visible.

For `workflow_dispatch`, the independent engine checks still run and an
additional reusable exact-SHA test-only ARSAS job provides manual coverage.

## Costs and bounds

The P0 immutable historical baseline is unchanged. CI-P2B reduces the
static workflow command maxima by **one more** duplicate .NET
restore/build/test, in addition to CI-P2A:

| Metric | CI-P1 | CI-P2A | CI-P2B |
| --- | ---: | ---: | ---: |
| `dotnet restore` occurrences | 16 | 15 | 14 |
| `dotnet build` occurrences | 16 | 15 | 14 |
| `dotnet test` occurrences | 18 | 17 | 16 |
| `actions/setup-dotnet` uses | 13 | 12 | 12 |
| `actions/setup-python` uses | 13 | 13 | 14 |

The +1 Python action ensures the same audited verifier runs reproducibly
on the Windows R10 engine test runner. Unlike the removed application build,
it does not trigger another compilation. Actual wall-time still depends
on canonical Build ARSAS, engine tests, runner load, and restore caches.

## Physical authority boundary

This milestone does **not** modify:

- SCL, Discovery, MMS, RCB or report runtime behavior;
- accepted R10 Ed1/Ed2 physical evidence;
- exact engine lock / historical physical tested head;
- ARIEC61850 engine tests or their post-merge authority;
- P141 SCL-assisted interoperability draft work;
- Golden Budget, Provenance, Repeat-run, Production or Field Capture guards;
- any release/installer workflow.

## Acceptance

Required: P0 integrity and budget, canonical Build ARSAS, Merge Execution
Guard, Mainline Readiness (engine full tests **plus** canonical ARSAS proof),
Field Capture smoke, Golden/Promotion guards — all on same exact PR head.

Regression rollback is narrow: restore the old application full-test step in
Mainline Readiness, leave independently retained engine/R10 validation and
canonical Build ARSAS untouched.

CI-P2C can later consolidate Field Capture packaging only with a separately
verified immutable binary artifact and all physical capture-specific checks.
No such retirement is authorized by P2B.
