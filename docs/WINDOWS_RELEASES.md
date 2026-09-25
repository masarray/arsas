# Windows release automation

ARSAS publishes two Windows x64 deliverables from the same reviewed source revision:

- `ARSAS-Windows-x64-Portable.exe` — a real self-contained single EXE for approved no-install use.
- `ARSAS-Windows-x64-Setup.exe` — the Inno Setup installer with Start Menu integration, uninstall support, and the same pinned ARIEC61850 and ArdIrec native-analysis revisions used by the release build.

The current stable application version is **1.6.40**. Public download metadata remains authoritative only after the corresponding tagged GitHub Release has been published and its checksums/provenance are visible. See the [v1.6.40 installed-release field acceptance](V1-6-40_INSTALLED_RELEASE_FIELD_ACCEPTANCE.md) for the exact physical Smart Discovery/static-reporting evidence, and the [v1.6.39 rejection record](../evidence/v1.6.39-physical-rejection.json) for the superseded release regression.

## Official stable release path

The canonical release request is `.release/windows.json`. A reviewed change to that file on `main` triggers `.github/workflows/release-windows.yml`.

The release workflow:

1. resolves the semantic version from the release request or tag and verifies it against `VERSION`, `Directory.Build.props`, and `ArIED61850Tester.csproj`;
2. resolves and checks out the immutable ARIEC61850 engine revision from `engines/ARIEC61850.lock.json`;
3. resolves and checks out the immutable ArdIrec revision from `engines/ARDIREC.lock.json`;
4. verifies source and licensing boundaries;
5. restores, builds, and runs the ARSAS regression suite against the exact release source;
6. exercises the managed native COMTRADE bridge, including cursor, phasor, harmonics, and distance-locus integration;
7. publishes the multi-file self-contained source used by the installer;
8. publishes and smoke-tests the self-contained portable single EXE;
9. compiles the Windows installer and performs silent install/uninstall smoke validation;
10. creates SHA-256 checksums, SPDX 2.3 SBOM, and provenance evidence;
11. creates GitHub artifact attestations for the public Windows binaries;
12. creates a new stable GitHub Release with the public assets; existing tags/assets are immutable and must not be overwritten.

The workflow explicitly rejects legacy `ardirec.exe` and Qt runtime files from official packaging. ARSAS uses the pinned in-process `ardirec_bridge.dll` contract instead.

## Historical publication workflows

The v1.6.38 golden-installer build/publisher and golden-runtime recovery workflows were one-off recovery mechanisms, not the ongoing release authority. Their tracked workflow definitions have been retired from the current tree because they could replace old published assets or incorrectly mark an older release as latest. Their immutable history and `.release/recover-v1.6.38-golden*.json` evidence remain available for audit; retirement does not rewrite published history or change the v1.6.40 binary.

The ongoing publisher is `.github/workflows/release-windows.yml`, governed by the reviewed `.release/windows.json` request and pinned app/engine/bridge source. The alternative verified-artifact publisher `.github/workflows/publish-verified-release.yml` refuses to overwrite an existing tag. Manual supply-chain backfill is additive only and refuses replacement of an existing published SBOM. Publication metadata and the website must follow the verified release rather than become a separate publication authority.

## Release workflow ownership

The active release automation has intentionally separate responsibilities. Maintainers should change the narrowest workflow that owns the required behavior rather than duplicating publication logic.

| Workflow | Responsibility | Public release mutation |
| --- | --- | --- |
| `.github/workflows/release-windows.yml` | Canonical Windows build, test, portable/installer packaging, checksums, SBOM, provenance and new stable publication from the reviewed release request. | May create a new release only; existing published tag/assets are treated as immutable. |
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
