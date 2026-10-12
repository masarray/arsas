# CI-P3 — Canonical Windows package artifact

Issue #444.

## Objective

Move Windows CI from repeated application rebuilds toward a build-once/package-many
model without weakening regression, native bridge, installer, physical or release
authority.

The central rule is:

> Test exact source once, seal exact package bytes, then package/promote those exact
> bytes after independently verifying their identity and hashes.

Rebuilding the same source is not treated as proof that the same binary was produced.

## Why exact-byte promotion

CI-P2F observed that two Windows single-file publishes using the same ARSAS source
and ARIEC61850 integration SHA were not byte-identical. CI-P3 therefore does not
require reproducible builds before eliminating downstream rebuilds.

Instead, Build ARSAS creates an immutable package artifact and downstream consumers
verify that artifact byte-for-byte before use.

Reproducible-build work remains useful hardening, but it is not the trust boundary.

## Canonical Build ARSAS responsibilities

After the normal full regression suite succeeds, Build ARSAS:

1. resolves the exact ARIEC61850 and ArdIrec lock SHAs;
2. checks out the exact ArdIrec revision;
3. builds and tests one native-only ArdIrec bridge;
4. publishes the installer-input folder using that exact bridge file;
5. runs managed native bridge and distance-locus integration tests against the
   bridge in the installer-input folder;
6. publishes the portable single EXE using the same bridge file;
7. smoke-tests the portable EXE;
8. stages the already-built ARSAS.Tests runtime plus the exact COMTRADE fixtures
   needed for package validation;
9. writes a hash-complete package manifest;
10. uploads ARSAS-windows-package-input.

The old ARSAS-win-x64-portable-single-exe artifact remains available for normal
portable testing and download.

## Sealed artifact contract

The package artifact contains:

- ci-windows-package-authority.json;
- canonical full-regression manifest and TRX;
- portable single EXE;
- complete self-contained installer-input folder;
- canonical prebuilt test runtime;
- pinned native integration fixtures.

The manifest binds:

- source SHA;
- ARIEC61850 SHA;
- ArdIrec SHA;
- Build ARSAS run ID, attempt and event;
- version/runtime;
- full-regression counters and TRX SHA-256;
- portable SHA-256;
- native bridge SHA-256;
- every payload file path, size and SHA-256.

It explicitly states that the installer has not yet been built and that the
artifact has no stable-release promotion authority.

## Cross-run verifier

verify-ci-package-reuse.py treats workflow artifacts as untrusted ZIP data.

Before extraction it verifies:

- repository/event/head/branch/run identity;
- exact source, engine and ArdIrec SHAs;
- latest matching Build ARSAS run semantics;
- completed failures are never reusable;
- safe normalized ZIP paths, no symlinks or encrypted entries;
- bounded file/archive sizes;
- manifest-to-archive file equality;
- every file SHA-256 and size;
- canonical TRX SHA-256 and all-pass counters;
- portable/native bridge special records;
- verification runtime and fixture references.

Only after all checks pass may it materialize files for installer use.

## Installer workflow

For pull_request and push/main, installer validation no longer restores, builds,
tests or publishes the ARSAS application.

It consumes the exact sealed package, then independently:

- verifies the physical-proven Smart Discovery source route without mutation;
- runs the packaged native bridge tests from the canonical prebuilt test runtime;
- compiles the Inno Setup installer from the verified installer-input directory;
- performs silent install/uninstall;
- rejects removed ArdIrec desktop/Qt runtime files;
- runs the same native bridge/locus tests against the installed bridge;
- writes checksums and uploads the installer artifact.

workflow_dispatch retains the historical local-build fallback so manual engineering
validation is not coupled to the existence of a cross-run package artifact.

## Authority boundaries

Build ARSAS owns application regression and sealed package bytes.

Installer workflow owns installer compilation plus installed-package smoke.

Smart Discovery physical evidence remains unchanged.

Stable release publication remains unchanged in this milestone. Existing
publish-verified-release.yml already demonstrates artifact-ID + SHA-256 publication
and is the preferred direction for the later release migration.

## Rollback

Restore installer PR/main source restore/build/test/publish/stage steps and stop
consuming ARSAS-windows-package-input.

Do not relax package identity, TRX, ZIP safety or SHA-256 checks to make reuse pass.
