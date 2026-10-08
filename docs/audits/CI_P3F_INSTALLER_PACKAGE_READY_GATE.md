# CI-P3F — Defer Windows installer allocation until canonical package readiness

Issue #460.

## Problem

On main commit 0814793c7473dbae1c1c64da56df12cbeecf5df7, the installer Windows runner
started at 22:18:39Z while the canonical Build ARSAS package was not uploaded until
22:24:11Z. The installer package-reuse step occupied the Windows runner from
22:19:01Z to 22:24:27Z: 326 seconds of mostly waiting before installer-specific work.

Installer-specific compile/install/runtime validation after package readiness took
about 152 seconds.

## Design

For pull_request and push events, installer-windows.yml now starts a small
ubuntu-latest package-ready job first. It only checks GitHub Actions metadata:

- exact Build ARSAS workflow name;
- exact event, head SHA and head branch;
- latest matching run ID/attempt;
- latest completed failure is rejected;
- exactly one non-expired ARSAS-windows-package-input artifact must exist.

The gate does not download the artifact archive and never validates or trusts package
bytes. Its sole purpose is scheduling: the Windows installer job is not allocated until
the canonical artifact metadata exists.

## Authority boundary

The Windows installer job still runs verify-ci-package-reuse.py. That verifier remains
the package authority and still validates:

- exact source, ARIEC61850 and ArdIrec SHAs;
- Build ARSAS run ID/attempt/event;
- canonical TRX digest and all-pass counters;
- every sealed payload file size and SHA-256;
- portable build identity and native bridge identity;
- package authority flags.

Installer compilation, silent install/uninstall, installed native runtime tests,
checksums and validated-installer sealing are unchanged.

Metadata presence is therefore not equivalent to package validity.

## Manual workflow_dispatch

Manual dispatch bypasses package-ready because it retains the existing local-build
fallback. The Windows job is allowed to start when either the metadata gate succeeds
or the event is workflow_dispatch.

## Static topology

No workflow file, checkout action, setup-python action, dotnet restore/build/test or
artifact-upload count is added. The change targets runtime allocation efficiency, not
textual CI primitive counts.

## Rollback

Remove package-ready and the installer needs/if dependency to return to direct Windows
polling. Do not remove or weaken verify-ci-package-reuse.py or installer/runtime smoke
as part of rollback.
