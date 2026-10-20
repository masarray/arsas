# CI P4 — Bounded Windows compute and explicit Field Capture (2026-10-21)

## Scope and immutable baseline

The user-accepted ARSAS application checkpoint remains **`5a33fb101026707caf6410ff097a569cfabf3939`** on `checkpoint/arsas-ctlmodel-glyph-command-stable-20261021`, documented in #651. CI optimization branch is separate, and modifies **no C#, XAML, native engine, IEC 61850 safety code, release workflow or ARIEC61850 lock**.

Existing #426 CI budget and #430 canonical exact-SHA proof architecture remain in force. Do not shrink security/physical gates to chase CI green.

## Measured baseline, not an estimate

Example user-accepted PR #650 at exact SHA `5a33fb10` (GitHub Actions run `39499030618`): 12 parallel PR workflow runs. The canonical `Build ARSAS` completed successfully in about 477 elapsed seconds; an additional `Smart Discovery Field Capture Build` completed in about 491 elapsed seconds on another Windows runner, consuming a previously sealed canonical package. Concurrent SCL R7 and physical-readiness workflows were also running. Elapsed wall-clock time and billable runner minutes are not equivalent; these are **observed per-run runtimes**, not invoices.

Some safeguards already exist: PR-number concurrency cancels superseded runs; the canonical `build.yml` produces pinned exact-source/engine TRX and sealed Windows package artifacts; several specialist workflows already reuse those artifacts. This change does **not** reinvent these proven features.

## P4-A: inexpensive fail-closed PR scope (Build ARSAS)

- New read-only `pr-scope` job uses `ubuntu-latest` + GitHub REST PR file list, **no checkout, .NET restore or packaging**. It emits `needs_windows=true|false`.
- Only a positively verified set of pure documentation paths (`README.md` and `docs/**/*.md`) may skip the canonical Windows job, **excluding** `docs/INTEROPERABILITY_REFERENCE_CONTRACT.md` because it carries release/IEC authority.
- All source code, tests, XAML, native/engine locks, safety evidence, scripts, assets, project configuration, workflows, unknown files or mixed PRs **always run** the full original Windows CI.
- Missing/empty PR file list, paginated result at or beyond GitHub's 3,000-file limit, permissions/API/network errors and unrecognized paths default to **full Windows CI**, never a skip.
- `push: main` and `workflow_dispatch` always run canonical Windows build/test/native smoke/package despite skipped preflight, preserving unchanged job name and build provenance.
- Note: a *document-only PR* cannot produce a portable artifact. Downstream users must not treat a skipped Windows job as package or physical authority. Specialist artifact verifiers remain fail-closed.

## P4-B: avoid duplicate Field Capture Windows packaging

- The specialist `Smart Discovery Field Capture Build` no longer starts an extra Windows packaging/smoke runner for every `*.cs`/XAML PR. Its canonical dependency is already provided by `Build ARSAS` on source-impacting PRs.
- Heavy `build-smart-capture` job now executes **only** after explicit PR label `ci-field-capture`, or via `workflow_dispatch`.
- `pull_request.types` includes `labeled` so engineers can request the specialist proof on the same PR without pushing an empty/noise commit. Updating a labeled PR still runs the specialist job on the new SHA.
- The existing manual exact-SHA reusable regression gate, canonical package digest/provenance verification, physical invariants, native CTest proof, portable smoke and artifact output are **unchanged** on opt-in paths. Status name retained; automatic unlabeled specialist job is intentionally skipped.
- This is no license to remove the specialist proof for actual smart discovery promotion or field-capture distribution: select the label/manual workflow before approval when that proof is needed.

## Anti-regression CI contract

- Workflows retain separate PR-scoped concurrency with `cancel-in-progress=true` and independent main/manual identities; no cross-PR cancellation and no alteration to artifact SHA acceptance.
- `scripts/test-ci-workflows.py` guards the fast-scope safe allowlist/fail-closed default, opt-in label semantics, dispatch path, existing canonical Windows job name and portable artifact ownership. CI P0 budget must remain green.
- Approval requires actual dry-run proof: a documentation-only PR skips `build-windows`; a source-impacting PR executes full Windows; a PR with `ci-field-capture` runs specialist capture; manually dispatched capture runs its full regression. CI jobs and check configurations are not a substitute for this scenario validation.
- Branch protection required checks are not readable from the GitHub connector (403); a path-filter or renamed job is **not** used. Existing job identifiers remain. Administrators must verify required-check configuration before promoting a workflow change to main.
- If the new PR is red, fail safe and preserve the accepted checkpoint. Do not disable a guard/physical authority to silence a pipeline.

## Expected savings and limits

- **Ordinary source-code PR:** eliminates **one** redundant ~491-second specialist Windows run per commit relative to PR #650, while preserving canonical Windows build and other IEC checks. This is an approximately **8.2-minute runner-occupancy reduction** on the measured case, **not** a guaranteed wall-clock speedup or GitHub bill credit.
- **Pure documentation PR:** avoids the entire ~477-second canonical Windows job, replaced by cheap Ubuntu scope. Exact savings depend on runner queues and repo-specific billing.
- **Explicit capture/physical-promotion PR:** extra specialist proof deliberately returns via `ci-field-capture` or manual dispatch.
- Additional historical SCL/physical-authority checks still overlap and may be consolidated only after exact-SHA parity is demonstrated, without weakening #651 or ARIEC native safety.
