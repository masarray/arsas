# CI-P1 — Reusable Exact-SHA Build/Test and Opt-in Shadow

Issue: [#428](https://github.com/masarray/arsas/issues/428)
Predecessor: [CI-P0 workflow inventory](CI_P0_WORKFLOW_BASELINE.md).

## Intent

CI-P1 introduces a reusable engine resolver, a conservative changed-area
classifier, and an exact-SHA Windows compilation/test primitive. Existing
workflows remain the canonical authorities until a measured parity gate is
satisfied. **Shadow PASS is not a production, release or physical-device proof.**

This phase intentionally introduces one extra .NET job *only on opt-in shadow
runs*. The P0 budget records that temporary overhead explicitly.

## Contracts and single owners

| Contract | New owner | Invariant |
| --- | --- | --- |
| Changed path classification | `scripts/classify-ci-changes.py` | Advisory, unknown/deleted changes cannot silently skip |
| Engine pin resolution | `scripts/resolve-ariec-lock.ps1` | Exact SHA, trusted repository, historical authority preserved |
| Composite resolver | `.github/actions/resolve-ariec-engine/action.yml` | Reuses resolver in the same job, no runner allocated |
| Build and test | `.github/workflows/_ci-p1-windows-build-test.yml` | One restore + build + full tests, no publish or release |
| Opt-in caller | `.github/workflows/ci-p1-shadow.yml` | Read-only PR merge SHA, conservative classifier, branch/label opt-in |
| Structural CI budget | `evidence/ci-workflow-budget.json` | Existing P0 baseline immutable; migration delta explicitly capped |

The canonical existing `build.yml`, IEC/SCL guards, physical evidence, RCB,
FAT/IO and SV workflows are unchanged.

## Reproduce classifier validation

```bash
python3 scripts/test-classify-ci-changes.py
python3 scripts/classify-ci-changes.py --paths Services/NativeIec61850Client.SclAssisted.cs
```

On a PR, `ci-p1-shadow.yml` fetches the synthetic GitHub PR merge commit with
two parents and inspects its diff against the first parent. This tests the
candidate **as it would merge into main**, not an arbitrary developer branch
snapshot. If a merge parent cannot be resolved or the diff is empty, the
classifier deliberately selects broad verification.

The classifier never disables a required legacy gate. It provides advisory
area flags so CI-P2 can make a reviewed targeted-job decision.

## Reproduce immutable lock resolver validation

```powershell
pwsh -NoProfile -File scripts/test-resolve-ariec-lock.ps1
pwsh -NoProfile -File scripts/resolve-ariec-lock.ps1 -LockPath engines/ARIEC61850.lock.json
```

The resolver only reads the lock. It rejects untrusted repositories, symbolic
engine commit refs, invalid hashes, and inconsistent historical accepted-layer
references. The reusable job then checks out that exact commit and verifies
`git rev-parse HEAD` before compilation. Historical physical SHA and current
build SHA remain separate manifest fields.

## Shadow opt-in

The Windows shadow runs when:

- the PR branch is `ci/p1-fast-gate-shadow` (CI-P1 self-validation);
- a PR has label `ci-p1-shadow`; or
- `workflow_dispatch` is explicitly requested on an available branch.

Otherwise the lightweight classifier runs (subject to path filters), while
legacy CI remains unchanged. For a labeled PR, the shadow only compiles if the
classifier indicates a .NET-impacting change.

The called Windows job does **not** create a public Release, publish an
installer, write evidence back to Git, or mutate GitHub settings.

## Exact-sha parity gate

Before a legacy job may be retired, compare:

1. The synthetic PR merge source SHA used by canonical Build ARSAS and P1.
2. The `engines/ARIEC61850.lock.json` resolved commit and the checked-out engine SHA.
3. Full regression test outcomes (not just selected tests), including failure
   diagnostics and TRX.
4. Coverage of source, release, field-authority and post-merge checks that are
   **not** included in the reusable build/test primitive.
5. The packaged binary SHA when packaging is eventually moved; a shadow TRX
   artifact alone must never serve as a release binary.

Shadow metadata uploads `ci-p1-shadow-manifest.json` and full test TRX as
short-retention, non-promotion evidence. It deliberately contains neither
source CID files nor physical device identifiers.

## Temporary migration cost

The P0 immutable observed baseline is still **29 workflows**, **15 restore**,
**15 build** and **17 test** after the P0 guard. CI-P1 adds two workflow files:
one lightweight classifier and one reusable Windows shadow. The guarded maximum
during migration is **31 workflows**, **16 restore**, **16 build** and **18 test**
invocations in workflow source.

These are static source counts, not simultaneous runtime invocations and not a
claim that CI is faster already. CI-P2 should reduce these counts through
verified replacement and artifact reuse, not by weakening guard semantics.

## Safety gate for promotion of P1

P1 may merge to main when P0 structural/classifier tests, reusable Windows
build/test, and unchanged legacy checks are green on the same tested PR merge
head. The post-merge P0 guard must remain green.

P1 does not approve reducing physical tests or disabling existing Build ARSAS.
A future CI-P2 PR must prove parity with accepted field evidence and tag/release
boundaries before retiring each specific legacy workflow.
