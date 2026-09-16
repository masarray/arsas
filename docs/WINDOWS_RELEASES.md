# Windows release automation

ARSAS publishes two Windows x64 deliverables from the same reviewed source revision:

- `ARSAS-Windows-x64-Portable.exe` — a real self-contained single EXE for approved no-install use.
- `ARSAS-Windows-x64-Setup.exe` — the Inno Setup installer with Start Menu integration, uninstall support, and the same pinned ARIEC61850 and ArdIrec native-analysis revisions used by the release build.

The current application version on `main` is **1.6.37**. Public download metadata remains authoritative only after the corresponding tagged GitHub Release has been published and its checksums/provenance are visible.

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
12. creates or updates the stable GitHub Release and uploads the public assets.

The workflow explicitly rejects legacy `ardirec.exe` and Qt runtime files from official packaging. ARSAS uses the pinned in-process `ardirec_bridge.dll` contract instead.

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

A manual run with publication disabled is useful for packaging verification, but it is **not** a public stable release and must not be used to invent `landing/latest.json` evidence.

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
.\scripts\publish-windows-portable.ps1 -Version 1.6.37
.\scripts\build-windows-installer.ps1 -Version 1.6.37 -Runtime win-x64
```

Official CI additionally supplies the pinned engine projects and ArdIrec source explicitly so the build cannot silently resolve an unreviewed revision.

## Release evidence and signing

A stable release is authoritative only when the tagged GitHub Release and its published evidence agree on version, assets, source commit, sizes, and SHA-256 values. The landing site's `latest.json` is synchronized from that verified publication evidence; it must not be advanced merely because `main` has a newer source version.

The current public Windows binaries are not assumed to be Authenticode-signed unless the release evidence says so. An unsigned release can trigger Windows SmartScreen. Verify the published SHA-256 value before use.
