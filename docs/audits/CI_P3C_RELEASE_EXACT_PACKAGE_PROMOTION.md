# CI-P3C — Exact sealed package promotion through Windows release

Issue #455.

## Objective

Complete the Windows build-once/package-many chain. Build ARSAS is the canonical
application regression and package-byte authority; the production release workflow
must package and publish those exact bytes rather than rebuilding equivalent source.

## Production path

For a reviewed `.release/windows.json` push on `main` and for `v*.*.*` tag
pushes, `release-windows.yml`:

1. checks out and verifies the exact release source SHA;
2. resolves release version, ARIEC61850 SHA and ArdIrec SHA from tracked locks;
3. queries the latest matching Build ARSAS **push/main** run for that exact SHA;
4. verifies the sealed `ARSAS-windows-package-input` before materialization;
5. requires exact source/engine/ArdIrec/run identity, all-pass canonical TRX,
   portable build identity and every payload file SHA-256;
6. copies the verified installer-input directory and portable EXE into release
   staging without rebuilding application bytes;
7. reruns the release-specific native bridge/locus verification;
8. reruns portable smoke;
9. compiles the Inno installer from the exact verified installer input;
10. performs silent install/uninstall and installed native bridge/locus tests;
11. generates checksums, SPDX SBOM, provenance and artifact attestations;
12. applies the existing immutable create-only stable publication contract.

A tag that points at a commit without an exact successful Build ARSAS push/main
package fails closed. It does not fall back to a production rebuild.

## Release-specific fixture

P3C does not replace the historical release-native fixture with the installer
fixture. Build ARSAS now seals both `p1-release-smoke.cfg` and its DAT companion
inside the shared package payload. The package manifest exposes
`releaseFixturePath`; the verifier confirms that path is part of the
hash-complete payload before release can use it.

## Manual fallback

`workflow_dispatch` keeps the historical local restore/build/test/folder publish/
portable publish path. This is an engineering recovery mechanism, not the normal
production promotion path. Existing `publish_release`, prerelease and immutable
published-tag behavior remain unchanged.

## Authority boundaries

- **Build ARSAS:** canonical application regression, native bridge build,
  installer-input bytes, portable bytes and sealed package manifest.
- **Release workflow:** release version policy, native release re-verification,
  Inno compilation, installed-package smoke, checksums, SBOM, provenance,
  attestations and public release creation.
- **Physical Smart Discovery evidence:** unchanged.

The sealed package itself still states `releasePromotionAuthority=false`.
Release authority is created only after the release-owned checks above succeed.

## Provenance

Release provenance remains schemaVersion 2 and adds:

- `canonicalPackageAuthority`;
- `canonicalPackageRun`;
- `canonicalPackageArtifactSha256`;
- `canonicalPortableSha256`.

Manual fallback records its authority as `manual-local-build`.

## CI accounting

Static restore/build/test counts do not fall because the manual fallback remains
tracked in YAML. Runtime work on production push/tag releases does fall: the
application restore/build/full regression, installer-input publish and portable
publish are no longer repeated.

## Rollback

Restore the non-manual release rebuild/publish steps and stop consuming the sealed
package. Do not weaken package identity, hashes, canonical TRX, build identity or
stable release immutability as a workaround.
