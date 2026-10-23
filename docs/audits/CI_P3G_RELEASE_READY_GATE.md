# CI-P3G — Release-ready metadata gate

Issue #462.

## Goal

Keep expensive Windows release capacity out of dependency polling.

Before P3G, the production release workflow allocated `windows-latest`
immediately and could then spend up to 1200 seconds waiting for the exact
canonical Build ARSAS package and another 1800 seconds waiting for the exact
validated installer.

P3F already moved canonical-package waiting out of the installer Windows job.
P3G applies the same orchestration principle to the release lane.

## Production release path

For non-manual release events, a lightweight `ubuntu-latest` `release-ready`
job:

1. checks out the exact release source SHA;
2. resolves and validates release version, ARIEC61850 SHA and ArdIrec SHA;
3. requires the latest exact Build ARSAS `push/main` run for the same source;
4. requires exactly one non-expired `ARSAS-windows-package-input` artifact;
5. requires the latest exact installer `push/main` run for the same source to
   be completed successfully;
6. requires exactly one non-expired versioned installer artifact;
7. emits only run/artifact identifiers and scheduling diagnostics.

The metadata gate never downloads artifact bytes and has
`releasePromotionAuthority=false`.

Only after the gate succeeds is the `windows-release` job allocated.

## Authority remains on Windows

The Windows release job still independently:

- validates/materializes the sealed Build ARSAS package;
- revalidates source, engine, ArdIrec, run/attempt, TRX and every payload hash;
- validates/materializes the installed-smoke-tested installer;
- rechecks portable and installer SHA-256 values;
- runs portable smoke;
- runs release-specific silent install/uninstall and installed native runtime
  verification;
- creates checksums and SPDX SBOM;
- writes provenance;
- produces attestations;
- enforces stable-release publication policy.

Metadata readiness cannot substitute for any of those checks.

## Tag behavior

A tag release still requires package and installer evidence from an exact
`push/main` run of the same commit. A tag on a commit that never completed the
main packaging/installer path fails closed instead of rebuilding silently.

## Manual recovery

`workflow_dispatch` bypasses `release-ready` and retains the historical local
restore/build/test/publish/Inno fallback. This preserves an explicit engineering
recovery path without weakening production exact-byte promotion.

## Artifact archive readiness hardening

Because the release Windows runner now starts close to installer artifact
publication, `verify-ci-installer-reuse.py` treats only archive-download HTTP
404 as transient within its existing bounded deadline.

- transient 404: retry;
- persistent 404: timeout and fail closed;
- any other HTTP failure: immediate hard failure.

The package verifier already follows the same observed GitHub artifact-readiness
rule.

## Static CI cost

P3G adds one checkout and one Python setup action on `ubuntu-latest`. It adds
no restore, build or test invocation.

This is an intentional scheduling trade: cheap metadata work is added to avoid
holding a Windows release runner while upstream immutable artifacts are still
being produced.

## Rollback

Remove the `release-ready` job and the `windows-release` dependency/condition.
Do not remove or weaken the package verifier, installer verifier, release smoke,
SBOM, provenance, attestations or publication policy.
