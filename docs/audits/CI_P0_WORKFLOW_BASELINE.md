# CI-P0 — Workflow Stabilization Baseline

Issue: #426

CI-P0 freezes the current GitHub Actions estate before any consolidation. It is deliberately non-semantic: no IEC 61850 runtime path, physical acceptance rule, release authority, or domain test contract is removed or weakened in this phase.

## Why P0 exists

The repository has accumulated many independently triggered workflows that repeat checkout, .NET setup, restore, build and test work. The failure mode is not only slower CI. Duplicated policy implementations drift, and an unrelated authority update can make several pipelines disagree about the same source of truth.

The P0 goal is therefore:

> make current CI topology observable, deterministic and corruption-resistant before optimizing it.

## Main baseline

Source: `71e8d7e864b22c2f2146e5f8576cef0126c5f6e7`.

Observed before adding the CI-P0 integrity workflow:

| Indicator | Count |
| --- | ---: |
| Tracked workflow files | 28 |
| `actions/checkout` uses | 31 |
| `actions/setup-dotnet` uses | 12 |
| `actions/setup-python` uses | 13 |
| `dotnet restore` invocations | 15 |
| `dotnet build` invocations | 15 |
| `dotnet test` invocations | 17 |
| `actions/upload-artifact` uses | 33 |
| Workflows containing a .NET build | 12 |
| Workflows containing a .NET test | 11 |
| Reusable `workflow_call` entry points | 0 |

These are textual cost indicators, not runtime-duration measurements. They intentionally over-simplify complex workflows so growth and consolidation can be reviewed consistently.

## CI-P0 guard

The tracked budget lives in:

`evidence/ci-workflow-budget.json`

The deterministic inventory lives in:

`scripts/audit-ci-workflows.py`

Offline regression tests live in:

`scripts/test-ci-workflows.py`

The guard performs two independent checks:

1. **Structural integrity**
   - exactly one top-level workflow `name`;
   - exactly one top-level `jobs` block;
   - no duplicate job IDs;
   - no duplicate step names inside the same job.

   Duplicate step names across different jobs remain legal. This catches the class of accidental copy/paste corruption that duplicated large sections of the R7 workflow without pretending to replace GitHub's YAML parser.

2. **CI-sprawl budget**
   - new workflows/builds/tests/checkouts cannot silently increase the frozen maxima;
   - a deliberate increase must update the budget in the same reviewed change;
   - reductions never require a budget increase.

The P0 workflow itself is intentionally cheap: one checkout plus standard-library Python. It performs no .NET restore/build/test and uploads no artifact.

## Ownership map

### Core build / package

- `.github/workflows/build.yml` — canonical PR/main Windows build, regression suite, portable EXE.
- `.github/workflows/installer-windows.yml` — installer-specific validation.
- `.github/workflows/release-windows.yml` — release-only Windows packaging and publication path.

P0 does not merge these responsibilities yet.

### IEC 61850 / SCL / physical authority

- `interoperability-reference-guard.yml`
- `scl-interoperability-r7.yml`
- `smart-discovery-capture-build.yml`
- `smart-discovery-golden-budget-lock.yml`
- `smart-discovery-golden-provenance.yml`
- `smart-discovery-mainline-readiness.yml`
- `smart-discovery-merge-execution-guard.yml`
- `smart-discovery-post-merge-production.yml`
- `smart-discovery-production-promotion.yml`
- `smart-discovery-repeat-run-stability.yml`
- `rcb-export-guard.yml`

These workflows currently encode multiple layers of historical, physical and promotion authority. P0 records them unchanged. P1/P2 may consolidate execution only after equivalent outcomes are proven.

### Focused technical gates

- `validate-io-testing.yml`
- `validate-sv-evidence.yml`
- `comtrade-viewer-integration.yml`
- `progressive-static-bench.yml`

### Product site / adoption / measurement

- `pages.yml`
- `adoption-proof.yml`
- `measurement-contract.yml`
- `search-growth.yml`
- `site-measurement.yml`
- `production-health.yml`

### Release metadata / supply chain

- `publish-verified-release.yml`
- `release-supply-chain.yml`
- `sync-release-documentation.yml`
- `sync-release-evidence.yml`

## P0 invariants

P0 must not:

- retire an existing workflow;
- weaken a required physical or release gate;
- change engine/runtime source;
- reinterpret historical acceptance evidence;
- make one current engine pin silently replace a historical tested baseline;
- change product behavior merely to satisfy CI.

P0 may:

- add read-only inventory;
- add corruption detection;
- freeze duplicate-CI growth;
- document ownership and dependencies;
- restore a workflow to its known-good baseline when accidental text corruption is proven.

## Handoff to CI-P1

CI-P1 should introduce the first reusable primitives and a single fast PR gate, but only after P0 is merged and stable.

Recommended P1 order:

1. reusable immutable engine resolver;
2. reusable .NET restore/build/test primitive;
3. one PR change classifier;
4. artifact reuse instead of repeated compilation;
5. repository-wide PR concurrency/cancellation;
6. shadow comparison before retiring any legacy workflow.

The P0 budget becomes the before-state. P1 is successful when the expensive counts decrease while semantic guards remain equivalent.
