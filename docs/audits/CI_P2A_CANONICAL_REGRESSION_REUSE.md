# CI-P2A — Canonical Regression Proof Reuse

Issue: [#430](https://github.com/masarray/arsas/issues/430)
Predecessor: [CI-P1 reusable/shadow architecture](CI_P1_SHADOW_CONTRACT.md).

## Scope and optimization

The Smart Discovery Merge Execution Guard used to re-run the same .NET
restore/build/full ARSAS regression suite already executed unconditionally by
canonical `Build ARSAS` on **every PR**.

P2A removes those duplicate PR compilation steps, **without** losing the
historical `Verify P0-5h ordered merge contract` check or its regression
requirement.

The specialized check now has two independent, fail-closed stages:

1. Validate the original ordered-merge evidence, scripts, PowerShell parser
   output and mutation-scope constraints.
2. Consume and independently verify the successful canonical Build ARSAS test
   artifact for the *same source and engine*. It may not pass on historical or
   stale green results.

This is a single-lane reduction, not permission to disable other Smart
Discovery, physical-retest, IEC 61850 or release checks.

## Canonical evidence producer

Canonical `.github/workflows/build.yml` still builds and tests normally.
After its successful full regression step, it runs
`scripts/write-ci-canonical-proof.py` and adds a tiny manifest to its **existing**
`ARSAS-test-evidence` artifact. No additional artifact upload job or
release/publication authority is created.

`ci-canonical-authority.json` binds:

- exact checked-out ARSAS SHA;
- exact checked-out ARIEC61850 SHA from the immutable lock;
- canonical workflow name, run ID and attempt;
- SHA-256 of the actual full-regression `arsas-tests.trx`;
- exact full test counters, including zero failed/not-executed;
- explicit `releasePromotionAuthority=false`.

The manifest is a CI claim, **not a cryptographic signature**, and does not
replace real physical IED evidence.

## Specialized consumer

`scripts/verify-ci-canonical-reuse.py` has access only to a token with
`actions: read` and `contents: read`. It:

1. locates the latest `Build ARSAS` pull-request workflow for the exact
   PR head SHA and branch (not an older successful attempt);
2. requires the run to be `completed/success`, including its portable
   package/smoke tests;
3. downloads only the canonical `ARSAS-test-evidence` artifact;
4. parses the ZIP **as data** without extracting or executing it;
5. validates artifact safety, manifest schema, exact synthetic PR merge SHA,
   exact engine SHA, workflow run/attempt ID, TRX content SHA-256 and all
   test counters;
6. rejects missing/incorrect artifacts, failed runs, conflicting test counts,
   stale merge trees and timeouts.

GitHub archive redirect handling removes bearer credentials on cross-origin
redirects to artifact blob storage.

The bounded waiting policy is 16 minutes; timeout is **FAIL**, not skipped.
A canonical failure is reflected as specialized check failure, regardless
of which checks are configured as branch-protection requirements.

## Manual execution

For `workflow_dispatch`, the P0-5h source guard still runs. The reusable
`_ci-p1-windows-build-test.yml` job supplies a fresh exact-SHA full test,
rather than requiring a nonexistent PR Build run. It remains test-only and
cannot promote a release or mutate physical evidence.

## Before and after

The immutable P0 observed baseline is unchanged and retained in
`evidence/ci-workflow-budget.json`. P2A changes the guarded *workflow-source*
maximums:

| Indicator | P1 maximum | P2A maximum |
| --- | ---: | ---: |
| Workflow files | 31 | 31 |
| setup-dotnet uses | 13 | 12 |
| .NET restore invocations | 16 | 15 |
| .NET build invocations | 16 | 15 |
| .NET test invocations | 18 | 17 |
| Workflows containing .NET build | 13 | 12 |
| Workflows containing .NET test | 12 | 11 |

Those are static counts, **not** evidence that every PR is 1/16 faster.
The removed duplicated Windows compilation frees compute and reduces
independent failure surfaces; the specialized guard still waits for canonical
completion on an Ubuntu runner, so its verdict cannot precede canonical CI.

## Validation and rollback

Offline synthetic fixtures:

```bash
python3 scripts/test-ci-canonical-reuse.py
python3 scripts/test-ci-workflows.py
```

Before merge, prove the P0 inventory guard, canonical Build ARSAS, new
specialized check, R10 readiness, Golden/Promotion and Field Capture workflows
all green on the same exact PR head and merge tree.

If the run metadata, artifact format or permission model creates unacceptable
flakiness, revert only the specialized verifier/consumer lane to the previous
direct build/test commands; **never disable** the P0-5h source contract, and
never treat failed proof reuse as a green skip.

Remaining CI-P2 work should target other duplicated lanes separately.
No other workflow retirement is authorized by this P2A change.
