# CI-P3D — Exact validated installer promotion

Issue #457.

## Objective

Complete the Windows build-once/package-many chain at the installer layer.
The normal production release path must publish the exact installer bytes that
were already compiled, silently installed, runtime-tested and uninstalled by the
installer validation workflow for the same source and canonical package.

## Installer authority

After installer compilation and installed runtime smoke pass,
`.github/workflows/installer-windows.yml` writes
`ci-installer-authority.json` into the existing versioned installer artifact.

The manifest binds:

- release version;
- exact ARSAS source SHA;
- exact ARIEC61850 SHA;
- exact ArdIrec SHA;
- canonical Build ARSAS package run;
- canonical package artifact SHA-256;
- installer workflow run and attempt;
- installer filename, SHA-256 and size;
- `installedSmokePassed=true`;
- `releasePromotionAuthority=false`.

The installer validator remains validation authority only. It cannot publish a
stable release by itself.

## Production release

For reviewed main release requests and tag releases,
`release-windows.yml`:

1. verifies and materializes the exact sealed Build ARSAS package as in P3C;
2. waits boundedly for the matching successful installer validation
   `push/main` run for the same source SHA;
3. validates the installer manifest and exact installer bytes;
4. requires the installer to reference the same canonical package artifact
   already accepted by the release;
5. promotes those exact installer bytes without installing Inno Setup or
   recompiling the installer;
6. still performs release-specific silent install/uninstall and installed
   native bridge/locus tests with the release fixture;
7. generates release checksums, SPDX SBOM, provenance and attestations;
8. preserves the existing create-only/immutable stable release contract.

A tag without an exact validated main installer fails closed.

## Manual fallback

`workflow_dispatch` retains local Inno Setup installation and installer
compilation. It records `manual-local-build` installer authority.

## Provenance

Release provenance adds:

- `validatedInstallerAuthority`;
- `validatedInstallerArtifactSha256`;
- `validatedInstallerSha256`.

These fields supplement the P3C canonical package provenance. They do not
replace source, engine, ArdIrec or portable identity.

## Authority boundaries

- **Build ARSAS:** tested application and canonical package bytes.
- **Installer validation:** installer compilation plus installed smoke authority.
- **Release workflow:** release-specific installed runtime re-verification,
  checksums, SBOM, provenance, attestations and public release creation.
- **Physical IEC 61850 evidence:** unchanged.

## Rollback

Restore production Inno Setup installation and installer compilation in the
release workflow. Do not weaken source/package/installer identity checks to make
promotion pass.
