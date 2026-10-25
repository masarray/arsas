# Windows release automation

ARSAS publishes two Windows x64 deliverables from the same reviewed source revision:

- `ARSAS-Windows-x64-Portable.exe` — a real self-contained single EXE for approved no-install use.
- `ARSAS-Windows-x64-Setup.exe` — the Inno Setup installer with Start Menu integration, uninstall support, and the same pinned ARIEC61850 and ArdIrec native-analysis revisions used by the release build.

The current public stable version is **[v1.6.42](https://github.com/masarray/arsas/releases/tag/v1.6.42)** (published 25 October 2026, source `3db2ca98d144ab4b456e8dc1db9af18ad7a948f2`, pinned ARIEC61850 trial `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`). The operator accepted the Windows application release and the public binary assets, tested through successful [Windows release workflow #40151975055](https://github.com/masarray/arsas/actions/runs/40151975055), remain unchanged. The GitHub release was subsequently promoted from prerelease metadata to Public Stable **without changing source/tag or binaries**. This is application distribution acceptance, not an assertion of physical R7/R10 authority, global IEC 61850 certification or lossless buffered SOE. The latest 25 October operator capture includes 38 continuity observations and 9 BufOvfl indications in 432 report frames. The [v1.6.40 physical acceptance](V1-6-40_INSTALLED_RELEASE_FIELD_ACCEPTANCE.md) applies only to its historical app/engine pair, not the new trial pin; see [PR #729](https://github.com/masarray/arsas/pull/729) for outstanding qualifications.

## Public download links and identity

- [Latest stable installer](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe): SHA-256 `71a3d6fdb01c303fdd85db61fb529f714d81ef6916140acb69c158a47206a173`.
- [Latest stable portable EXE](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Portable.exe): SHA-256 `281dc1d5f649fb28d3cbbeec858639103644a43296a225e44378fe9de4762669`.
- [Checksums](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SHA256SUMS.txt), [SPDX SBOM](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-SBOM.spdx.json) and [provenance](https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-PROVENANCE.json) describe the same public v1.6.42 assets.

The `main` branch's `VERSION` and app sources can lag the public GitHub release when a validated RC tag has been published independently. **Publication authority belongs to the latest stable tagged GitHub release and its immutable assets, not to the working tree on `main`.** Do not bump `.release/windows.json` or re-run packaging simply to refresh README/landing. For documentation-only synchronization use existing `sync-release-documentation.yml`, which reads GitHub latest stable release and normalizes the site and updater evidence. `sync-release-evidence.yml` mirrors `.release/published.json` to the `release-evidence` branch consumed by Pages and the updater.

## Official stable release path

The canonical release request is `.release/windows.json`. A reviewed change to that file on `main` triggers `.github/workflows/release-windows.yml`.

The production release workflow:

1. resolves the semantic version from the release request or tag and verifies it against `VERSION`, `Directory.Build.props`, and `ArIED61850Tester.csproj`;
2. resolves the immutable ARIEC61850 and ArdIrec lock SHAs without rebuilding those dependencies;
3. verifies source, licensing and physical-proven Smart Discovery route boundaries;
4. requires the exact successful Build ARSAS `push/main` sealed Windows package for the same source, engine and ArdIrec SHAs;
5. validates the canonical all-pass TRX, portable build identity and SHA-256/size of every sealed payload file before materialization;
6. promotes the exact tested installer-input folder and portable EXE bytes instead of rebuilding the application;
7. reruns the release-specific managed native COMTRADE bridge/locus verification and portable runtime smoke;
8. compiles the Windows installer from the verified installer input and performs silent install/uninstall plus installed native bridge validation;
9. creates SHA-256 checksums, SPDX 2.3 SBOM, and provenance evidence that records the canonical package run/artifact identity;
10. creates GitHub artifact attestations for the public Windows binaries;
11. creates a new stable GitHub Release with the public assets; existing tags/assets are immutable and must not be overwritten.

A production tag whose exact commit has no matching canonical Build ARSAS package fails closed rather than silently rebuilding. `workflow_dispatch` retains an explicit local rebuild fallback for controlled engineering recovery.

The workflow explicitly rejects legacy `ardirec.exe` and Qt runtime files from official packaging. ARSAS uses the pinned in-process `ardirec_bridge.dll` contract instead.

## Historical publication workflows

The v1.6.38 golden-installer build/publisher and golden-runtime recovery workflows were one-off recovery mechanisms, not the ongoing release authority. Their tracked workflow definitions have been retired from the current tree because they could replace old published assets or incorrectly mark an older release as latest. Their immutable history and `.release/recover-v1.6.38-golden*.json` evidence remain available for audit; retirement does not rewrite published history or change the v1.6.40 binary.

The ongoing publisher is `.github/workflows/release-windows.yml`, governed by the reviewed `.release/windows.json` request and pinned app/engine/bridge source. The alternative verified-artifact publisher `.github/workflows/publish-verified-release.yml` refuses to overwrite an existing tag. Manual supply-chain backfill is additive only and refuses replacement of an existing published SBOM. Publication metadata and the website must follow the verified release rather than become a separate publication authority.

## Release workflow ownership

The active release automation has intentionally separate responsibilities. Maintainers should change the narrowest workflow that owns the required behavior rather than duplicating publication logic.

| Workflow | Responsibility | Public release mutation |
| --- | --- | --- |
| `.github/workflows/release-windows.yml` | Exact-byte promotion from the canonical Build ARSAS package and the matching installed-smoke-tested installer, release-specific runtime re-verification, checksums, SBOM, provenance and new stable publication from the reviewed release request. Manual dispatch retains local installer compilation fallback. | May create a new release only; existing published tag/assets are treated as immutable. |
| `.github/workflows/installer-windows.yml` | Installer/portable packaging and smoke validation for engineering verification. | No stable GitHub Release publication authority. |
| `.github/workflows/publish-verified-release.yml` | Alternative publication from already-tested workflow artifacts and an explicit verified publication request. | Create-only; refuses an existing tag rather than replacing it. |
| `.github/workflows/release-supply-chain.yml` | Verify an existing stable release and add missing supply-chain evidence/attestation. | Additive only; refuses replacement of an existing published SBOM. |
| `.github/workflows/sync-release-documentation.yml` | Synchronize website/release documentation from verified existing release evidence. | Does not build or replace Windows packages. |
| `.github/workflows/sync-release-evidence.yml` | Validate `.release/published.json`, mirror updater evidence to the dedicated `release-evidence` branch and request a website refresh. | Does not mutate the stable release or write runtime source to `main`. |

The packaging scripts under `scripts/` remain active implementation details of these workflows. Do not remove a script merely because it is not called from application code; verify its workflow consumer first.

## Public assets

A successful stable release publishes these stable asset names:

- `ARSAS-Windows-x64-Setup.exe`
- `ARSAS-Windows-x64-Portable.exe`
- `ARSAS-Windows-x64-SHA256SUMS.txt`
- `ARSAS-Windows-x64-SBOM.spdx.json`
- `ARSAS-Windows-x64-PROVENANCE.json`

Versioned build artifacts may also exist inside the workflow run, but public documentation and download buttons use the stable asset names above.

## Manual release build

For a controlled manual run, use **Actions → Release ARSAS Windows packages → Run workflow**. Supply a semantic version matching the checked-out ARSAS metadata and choose whether the workflow should publish a GitHub Release.

A manual run with publication disabled is useful for packaging verification, but it is **not** a public stable release and must not be used to invent `landing/latest.json` evidence. An existing tag can be verified, but it must not be republished with different bytes or a different source; use a new version for changed packages.

## Installer behavior

The installer:

- installs the self-contained ARSAS application to the selected Windows scope;
- creates a Start Menu shortcut;
- registers a standard Windows uninstaller;
- preserves upgrade compatibility through a stable Inno Setup `AppId`;
- carries the pinned ARIEC61850 runtime dependencies required by the application;
- carries `Tools/ArdIrec/ardirec_bridge.dll` and rejects the retired ArdIrec desktop/Qt runtime;
- is smoke-tested by CI using a silent current-user installation and uninstall path.

Npcap is not bundled. Install it separately according to its license and the engineering-workstation policy. MMS, SCL, monitoring, file transfer, and in-process COMTRADE analysis do not require ARSAS to install Npcap; raw-Ethernet GOOSE and Sampled Values capture do require an approved Npcap installation and suitable capture permission.

## Portable single EXE

The portable public package is one self-contained executable:

```text
ARSAS-Windows-x64-Portable.exe
```

The pinned ArdIrec bridge is embedded into the single-file bundle and materialized into the user's ARSAS native cache when needed. The portable package does not bypass AppLocker, WDAC, SmartScreen, antivirus, download-zone, or enterprise execution policy.

## Local packaging

Prerequisites:

- Windows 10 or Windows 11 x64;
- .NET 8 SDK;
- a compatible ARIEC61850 source checkout;
- the exact ArdIrec source revision required by `engines/ARDIREC.lock.json`;
- Inno Setup 6 for installer compilation.

Use the repository packaging scripts rather than hand-assembling a release folder. For example:

```powershell
$version = (Get-Content .\VERSION -Raw).Trim()
.\scripts\publish-windows-portable.ps1 -Version $version
.\scripts\build-windows-installer.ps1 -Version $version -Runtime win-x64
```

Official CI additionally supplies the pinned engine projects and ArdIrec source explicitly so the build cannot silently resolve an unreviewed revision.

## Release evidence and signing

A stable release is authoritative only when the tagged GitHub Release and its published evidence agree on version, assets, source commit, sizes, and SHA-256 values. The landing site's `latest.json` is synchronized from that verified publication evidence; it must not be advanced merely because `main` has a newer source version.

The current public Windows binaries are not assumed to be Authenticode-signed unless the release evidence says so. An unsigned release can trigger Windows SmartScreen. Verify the published SHA-256 value before use.
