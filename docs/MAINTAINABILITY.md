# Repository maintainability contract

ARSAS is maintained as production engineering software. Repository cleanliness is not cosmetic: source identity, evidence provenance, release authority, review ownership, and a small number of current workstreams must remain easy to audit.

## Sources of truth

| Concern | Current authority |
| --- | --- |
| Published stable binary | Latest immutable GitHub Release and its published SHA-256/provenance |
| Application version | `Directory.Build.props`, `VERSION`, and project metadata validated by CI |
| IEC 61850 engine revision | `engines/ARIEC61850.lock.json` |
| Windows release workflow | `.github/workflows/release-windows.yml` |
| CI source identity | Exact event SHA verified by `.github/workflows/build.yml` |
| Physical interoperability acceptance | Sanitized records in `evidence/` and linked engineering documentation |
| Independent-development boundary | `docs/INDEPENDENT_IMPLEMENTATION_AND_PROVENANCE.md` and `docs/CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md` |

A historical branch, pull request, artifact, or recovery manifest is never a newer authority merely because it contains more code or more detailed notes.

## Branch and pull-request lifecycle

- Start new implementation work from current `main` unless a documented stacked-PR dependency requires otherwise.
- Do not revive a stale release/discovery branch by merging or rebasing it wholesale onto current `main`.
- Close superseded PRs with a comment that identifies the replacement authority and any genuinely unmerged idea.
- Keep a draft PR open only while it has a current owner, a current base, and a concrete validation plan.
- Preserve release tags and historical commits. Do not rewrite published release history for cosmetic cleanup.
- Branch deletion is repository hygiene, not provenance deletion; public commit/PR history can remain reachable.

## Change boundaries

Prefer one engineering concern per PR. In particular, keep these separate unless the change is inseparable:

- IEC 61850 runtime/discovery/reporting behavior;
- SCL semantic/export behavior;
- release and CI infrastructure;
- website/public wording;
- provenance/evidence normalization;
- repository-only refactoring.

A naming cleanup must not silently change engine locks, runtime behavior, field acceptance metrics, DataSet order, release hashes, or protocol traffic.

## Large-file strategy

Large application files are reduced by extracting cohesive responsibilities, not by mechanically splitting files.

Before extracting a subsystem:

1. identify its state ownership and callers;
2. lock current behavior with focused regression tests;
3. extract pure transformation/validation logic before network or UI state;
4. keep protocol state machines in ARIEC61850 rather than WPF/application handlers;
5. measure allocations/latency before introducing caches, pools, or concurrency;
6. merge small steps and rerun combined-head CI.

Priority candidates are pure semantic normalization, SCL validation/transformation, report-plan construction, and presentation mapping. Association lifecycle, report activation, and physical-control paths require stricter evidence and must not be refactored merely to reduce line count.

## CI and release discipline

CI must build, test, and package the exact immutable source revision that triggered the run. Release publication must fail closed if source identity, engine lock, version metadata, package hashes, or published assets diverge.

Historical release-recovery workflows must not regain write authority. Published stable assets are immutable; a changed binary requires a new version/tag.

## Review and ownership

`.github/CODEOWNERS` identifies the current maintainer for repository-wide and high-risk surfaces. CODEOWNERS is a review-routing aid, not a substitute for branch protection, CI, provenance review, or subsystem-specific field validation.

For changes to release, engine locks, evidence, protocol integration, or source-clean policy, reviewers should verify both the code diff and the authority being changed.

## Definition of done

A maintainability change is complete when:

- it reduces ambiguity, duplication, stale authority, or unsafe coupling;
- it has no hidden runtime/release behavior change;
- relevant regression tests and full CI pass;
- documentation points to current authority;
- stale workstreams are closed or clearly marked historical;
- stable release evidence remains reproducible and unchanged unless a separately validated release supersedes it.
