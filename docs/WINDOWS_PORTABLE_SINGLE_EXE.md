# Windows portable single EXE

ARSAS publishes a self-contained Windows x64 portable executable named `ARSAS-Windows-x64-Portable.exe`.

## What portable means

- One file is downloaded and copied to the workstation.
- The .NET 8 runtime, WPF runtime, ARIEC61850 engine and managed packet-capture assemblies are bundled.
- The application manifest requests `asInvoker`; ARSAS itself does not request elevation or install services.
- The executable can start from a normal user-writable folder without an installed .NET runtime.

The .NET single-file host extracts bundled native and content payloads into the current user's bundle cache on first launch. This is normal single-file behavior and does not install ARSAS system-wide.

## Locked workstation boundary

A portable executable cannot bypass Windows or company security policy. Execution can still be blocked by AppLocker, Windows Defender Application Control, SmartScreen, antivirus policy, download-zone policy, or a read-only user profile. ARSAS binaries are currently not Authenticode-signed, so users should verify the published SHA-256 and follow their organization's approval process.

MMS engineering over TCP port 102 generally does not require administrator rights once the approved network path and firewall policy are available. GOOSE and Sampled Values use raw Ethernet capture and require Npcap to have been installed and approved by an administrator. The portable executable does not install Npcap and cannot bypass driver or capture-permission restrictions.

## Release validation

The Windows CI and release workflow enforce all of the following:

1. `PublishSingleFile=true` and `SelfContained=true` for `win-x64`.
2. Trimming remains disabled for WPF, reflection and packet-capture compatibility.
3. Native libraries and required content are bundled for user-cache extraction.
4. The publish directory contains exactly one file before the versioned EXE is staged.
5. The EXE runs `--portable-smoke-test`, loads the engine and Npcap managed assemblies, locates immutable engine provenance, writes to the user temporary directory and exits successfully.
6. Installer packaging remains based on the separately validated multi-file publish directory.
